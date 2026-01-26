using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Ocelot.DependencyInjection;
using Ocelot.Middleware;
using OrderDispatcher.Apigateway;
using OrderDispatcher.Apigateway.Dtos;
using System.Text;
using System.Text.Json;
using System.Net.Http.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("ocelot.json", optional: false, reloadOnChange: true);

var jwtOptions = builder.Configuration
    .GetSection("JwtTokenOptions")
    .Get<JwtTokenOptions>()
    ?? throw new InvalidOperationException("JwtTokenOptions section missing in configuration");

// ✅ CORS (herkese açık)
builder.Services.AddCors(options =>
{
    options.AddPolicy("OpenCors", policy =>
    {
        policy
            .AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader();
    });
});

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = "GatewayAuth";
        options.DefaultChallengeScheme = "GatewayAuth";
    })
    .AddJwtBearer("GatewayAuth", options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Key))
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddHttpClient("FileService", client =>
{
    var baseUrl = builder.Configuration["FileService:BaseUrl"];
    if (!string.IsNullOrWhiteSpace(baseUrl))
    {
        client.BaseAddress = new Uri(baseUrl);
    }
});

builder.Services.AddHttpClient("EngagementService", client =>
{
    var baseUrl = builder.Configuration["EngagementService:BaseUrl"];
    if (!string.IsNullOrWhiteSpace(baseUrl))
    {
        client.BaseAddress = new Uri(baseUrl);
    }
});

builder.Services.AddOcelot(builder.Configuration);

var app = builder.Build();

app.UseCors("OpenCors");

app.Map("/aggregate/engagement/stores-with-images", aggregateApp =>
{
    aggregateApp.Run(async context =>
    {
        if (!HttpMethods.IsGet(context.Request.Method))
        {
            context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            return;
        }

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        var httpClientFactory = context.RequestServices.GetRequiredService<IHttpClientFactory>();
        var config = context.RequestServices.GetRequiredService<IConfiguration>();

        var engagementClient = httpClientFactory.CreateClient("EngagementService");
        var engagementPath = config["EngagementService:StoresPath"] ?? "/api/engagement/store/getAll";
        using var storeRequest = new HttpRequestMessage(HttpMethod.Get, engagementPath);

        if (context.Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            storeRequest.Headers.TryAddWithoutValidation("Authorization", authHeader.ToString());
        }

        using var storeResponse = await engagementClient.SendAsync(storeRequest, context.RequestAborted);
        if (!storeResponse.IsSuccessStatusCode)
        {
            context.Response.StatusCode = (int)storeResponse.StatusCode;
            return;
        }

        var storeJson = await storeResponse.Content.ReadAsStringAsync(context.RequestAborted);
        var stores = DeserializeStores(storeJson, jsonOptions);

        var masterIds = stores
            .Select(s => s.ImageMasterId)
            .Where(id => id > 0)
            .Distinct()
            .ToArray();

        var imageMap = new Dictionary<int, string[]>();
        if (masterIds.Length > 0)
        {
            var fileClient = httpClientFactory.CreateClient("FileService");
            var filePath = config["FileService:ImagesByMasterIdsPath"] ?? "/images/getByMasterIds";
            using var fileRequest = new HttpRequestMessage(HttpMethod.Post, filePath)
            {
                Content = JsonContent.Create(new ImagesByIdsRequest(masterIds), options: jsonOptions)
            };

            if (context.Request.Headers.TryGetValue("Authorization", out authHeader))
            {
                fileRequest.Headers.TryAddWithoutValidation("Authorization", authHeader.ToString());
            }

            using var fileResponse = await fileClient.SendAsync(fileRequest, context.RequestAborted);
            if (fileResponse.IsSuccessStatusCode)
            {
                var imageItems = await fileResponse.Content.ReadFromJsonAsync<List<ImageMasterDto>>(jsonOptions, context.RequestAborted)
                                 ?? new List<ImageMasterDto>();
                imageMap = imageItems.ToDictionary(x => x.MasterId, x => x.ImageUrls);
            }
        }

        var result = stores.Select(store => new StoreWithImagesDto
        {
            UserId = store.UserId,
            FirstName = store.FirstName,
            LastName = store.LastName,
            PhoneNumber = store.PhoneNumber,
            Email = store.Email,
            UserName = store.UserName,
            ImageMasterId = store.ImageMasterId,
            ImageUrls = imageMap.TryGetValue(store.ImageMasterId, out var urls)
                ? urls
                : Array.Empty<string>()
        });

        context.Response.StatusCode = StatusCodes.Status200OK;
        await context.Response.WriteAsJsonAsync(result, jsonOptions, context.RequestAborted);
    });
});

// ✅ CORS MIDDLEWARE SIRASI ÖNEMLİ: Ocelot’tan ÖNCE
app.UseAuthentication();
app.UseAuthorization();

await app.UseOcelot();

app.Run();

static List<StoreDto> DeserializeStores(string json, JsonSerializerOptions options)
{
    try
    {
        var wrapped = JsonSerializer.Deserialize<StoreListResponseDto>(json, options);
        if (wrapped?.Value != null && wrapped.Value.Count > 0)
        {
            return wrapped.Value;
        }
    }
    catch (JsonException)
    {
    }

    return JsonSerializer.Deserialize<List<StoreDto>>(json, options) ?? new List<StoreDto>();
}
    
