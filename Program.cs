using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Ocelot.DependencyInjection;
using Ocelot.Middleware;
using OrderDispatcher.Apigateway;
using System.Text;

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
builder.Services.AddOcelot(builder.Configuration);

var app = builder.Build();

// ✅ CORS MIDDLEWARE SIRASI ÖNEMLİ: Ocelot’tan ÖNCE
app.UseCors("OpenCors");

app.UseAuthentication();
app.UseAuthorization();

await app.UseOcelot();

app.Run();
    