using Microsoft.AspNetCore.WebUtilities;
using OrderDispatcher.Apigateway.Dtos;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace OrderDispatcher.Apigateway;

public static class AggregateActions
{
    public static IApplicationBuilder MapAggregateEndpoints(this IApplicationBuilder app)
    {
        app.Map("/aggregate/engagement/stores-with-images", aggregateApp =>
        {
            aggregateApp.Run(AggregateStoresWithImages);
        });


        app.Map("/aggregate/catalog/products-with-images", aggregateApp =>
        {
            aggregateApp.Run(AggregateProductWithImages);
        });

        app.Map("/aggregate/order-management/basketDetail", aggregateApp =>
        {
            aggregateApp.Run(AggregateBasketDetail);
        });

        return app;
    }

    private static async Task AggregateProductWithImages(HttpContext context)
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

        var catalogClient = httpClientFactory.CreateClient("CatalogService");
        var productPath = config["CatalogService:ProductPath"];

        var storeId = context.Request.Query["storeId"].FirstOrDefault();

        var requestPath = productPath + storeId;

        using var catalogRequest = new HttpRequestMessage(HttpMethod.Get, requestPath);

        if (context.Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            catalogRequest.Headers.TryAddWithoutValidation("Authorization", authHeader.ToString());
        }

        using var catalogResponse = await catalogClient.SendAsync(catalogRequest, context.RequestAborted);
        if (!catalogResponse.IsSuccessStatusCode)
        {
            context.Response.StatusCode = (int)catalogResponse.StatusCode;
            return;
        }

        var productJson = await catalogResponse.Content.ReadAsStringAsync(context.RequestAborted);

        var products = JsonSerializer.Deserialize<List<ProductDto>>(productJson, jsonOptions);

        var masterIds = products?
            .Select(s => s.ImageMasterId)
            .Where(id => id > 0)
            .Distinct()
            .ToArray();

        var imageMap = new Dictionary<int, string[]>();
        if (masterIds?.Length > 0)
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

        var result = products?.Select(product => new ProductDto
        {
            Id = product.Id,
            Name = product.Name,
            Description = product.Description,
            Price = product.Price,
            Stock = product.Stock,
            Order = product.Order,
            BrandId = product.BrandId,
            CategoryId = product.CategoryId,
            ImageMasterId = product.ImageMasterId,
            ImageUrls = imageMap.TryGetValue(product.ImageMasterId, out var urls)
                ? urls
                : Array.Empty<string>()
        });

        context.Response.StatusCode = StatusCodes.Status200OK;
        await context.Response.WriteAsJsonAsync(result, jsonOptions, context.RequestAborted);
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
        var engagementPath = config["EngagementService:StoresPath"];
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
            StoreId = store.UserId,
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

    private static async Task AggregateBasketDetail(HttpContext context)
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

        var orgerManagementClient = httpClientFactory.CreateClient("OrderManagementService");
        var basketPath = config["OrderManagementService:BasketPath"];

        var storeId = context.Request.Query["storeId"].FirstOrDefault();
        var userId = context.Request.Query["userId"].FirstOrDefault();

        var requestPath = basketPath + "?userId=" + userId + "&storeId=" + storeId;

        using var basketRequest = new HttpRequestMessage(HttpMethod.Get, requestPath);

        if (context.Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            basketRequest.Headers.TryAddWithoutValidation("Authorization", authHeader.ToString());
        }

        using var basketResponse = await orgerManagementClient.SendAsync(basketRequest, context.RequestAborted);
        if (!basketResponse.IsSuccessStatusCode)
        {
            context.Response.StatusCode = (int)basketResponse.StatusCode;
            return;
        }

        var res = await basketResponse.Content.ReadAsStringAsync(context.RequestAborted);

        var basket = JsonSerializer.Deserialize<BasketDetailDto>(res, jsonOptions);

        var productIds = basket?.Items
            .Select(s => s.ProductId)
            .Where(id => id > 0)
            .Distinct()
            .ToArray();

        if (productIds?.Length > 0)
        {
            var catalogClient = httpClientFactory.CreateClient("CatalogService");
            var productPath = config["CatalogService:ProductListByIdsPath"];

            requestPath = QueryHelpers.AddQueryString(
                        productPath,
                        productIds.Select(id => new KeyValuePair<string, string>("productIds", id.ToString()))
                        );

            using var catalogRequest = new HttpRequestMessage(HttpMethod.Post, requestPath);
            catalogRequest.Content = new StringContent(JsonSerializer.Serialize(productIds), Encoding.UTF8,"application/json");

            if (context.Request.Headers.TryGetValue("Authorization", out authHeader))
            {
                catalogRequest.Headers.TryAddWithoutValidation("Authorization", authHeader.ToString());
            }

            using var catalogResponse = await catalogClient.SendAsync(catalogRequest, context.RequestAborted);

            if (!catalogResponse.IsSuccessStatusCode)
            {
                context.Response.StatusCode = (int)catalogResponse.StatusCode;
                return;
            }

            var productJson = await catalogResponse.Content.ReadAsStringAsync(context.RequestAborted);

            var products = JsonSerializer.Deserialize<List<ProductDto>>(productJson, jsonOptions);

            var masterIds = products?
                .Select(s => s.ImageMasterId)
                .Where(id => id > 0)
                .Distinct()
                .ToArray();

            var imageMap = new Dictionary<int, string[]>();
            if (masterIds?.Length > 0)
            {
                var fileClient = httpClientFactory.CreateClient("FileService");
                var filePath = config["FileService:ImagesByMasterIdsPath"];
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


            var result = basket.Items?.Select(product => new ProductDto
            {
            });

            context.Response.StatusCode = StatusCodes.Status200OK;
            await context.Response.WriteAsJsonAsync(result, jsonOptions, context.RequestAborted);
        }
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
