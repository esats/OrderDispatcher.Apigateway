using System.Net.Http.Json;
using System.Text.Json;
using OrderDispatcher.Apigateway.Dtos;

namespace OrderDispatcher.Apigateway;

public static class AggregateActions
{
    public static IApplicationBuilder MapAggregateEndpoints(this IApplicationBuilder app)
    {
        app.Map("/aggregate/engagement/stores-with-images", aggregateApp =>
        {
            aggregateApp.Run(AggregateStoresWithImages);
        });

        return app;
    }

    private static async Task AggregateStoresWithImages(HttpContext context)
    {
        if (!HttpMethods.IsGet(context.Request.Method))
        {
            context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            return;
        }

        if (context.User?.Identity?.IsAuthenticated != true)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        var services = context.RequestServices;
        var httpClientFactory = services.GetRequiredService<IHttpClientFactory>();
        var config = services.GetRequiredService<IConfiguration>();

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
    }

    private static List<StoreDto> DeserializeStores(string json, JsonSerializerOptions options)
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
}
