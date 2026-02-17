using Microsoft.AspNetCore.WebUtilities;
using OrderDispatcher.Apigateway.Dtos;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace OrderDispatcher.Apigateway;

public static class AggregateActions
{
    private const string AuthorizationHeader = "Authorization";
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

        app.Map("/aggregate/order-management/orders", aggregateApp =>
        {
            aggregateApp.Run(AggregateOrders);
        });

        app.Map("/aggregate/order-management/customerOrders", aggregateApp =>
        {
            aggregateApp.Run(AggregateCustomerOrders);
        });

        return app;
    }

    private static async Task AggregateProductWithImages(HttpContext context)
    {
        if (!EnsureGetAndAuthenticated(context))
        {
            return;
        }

        var jsonOptions = CreateJsonOptions();
        var (httpClientFactory, config) = GetServices(context);

        var catalogClient = httpClientFactory.CreateClient("CatalogService");
        var productPath = config["CatalogService:ProductPath"];

        var storeId = context.Request.Query["storeId"].FirstOrDefault();

        var requestPath = productPath + storeId;

        using var catalogRequest = new HttpRequestMessage(HttpMethod.Get, requestPath);
        CopyAuthorizationHeader(context, catalogRequest);

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

        var imageMap = await LoadImagesByMasterIds(
            context,
            httpClientFactory,
            config["FileService:ImagesByMasterIdsPath"],
            masterIds,
            jsonOptions);

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
        if (!EnsureGetAndAuthenticated(context))
        {
            return;
        }

        var jsonOptions = CreateJsonOptions();
        var (httpClientFactory, config) = GetServices(context);

        var engagementClient = httpClientFactory.CreateClient("EngagementService");
        var engagementPath = config["EngagementService:StoresPath"];
        using var storeRequest = new HttpRequestMessage(HttpMethod.Get, engagementPath);

        CopyAuthorizationHeader(context, storeRequest);

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

        var imageMap = await LoadImagesByMasterIds(
            context,
            httpClientFactory,
            config["FileService:ImagesByMasterIdsPath"],
            masterIds,
            jsonOptions);

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
        if (!EnsureGetAndAuthenticated(context))
        {
            return;
        }

        var jsonOptions = CreateJsonOptions();
        var (httpClientFactory, config) = GetServices(context);

        var orgerManagementClient = httpClientFactory.CreateClient("OrderManagementService");
        var basketPath = config["OrderManagementService:BasketPath"];

        var storeId = context.Request.Query["storeId"].FirstOrDefault();
        var userId = context.Request.Query["userId"].FirstOrDefault();

        var requestPath = basketPath + "?userId=" + userId + "&storeId=" + storeId;

        using var basketRequest = new HttpRequestMessage(HttpMethod.Get, requestPath);

        CopyAuthorizationHeader(context, basketRequest);

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
            catalogRequest.Content = new StringContent(JsonSerializer.Serialize(productIds), Encoding.UTF8, "application/json");

            CopyAuthorizationHeader(context, catalogRequest);

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

            var imageMap = await LoadImagesByMasterIds(
                context,
                httpClientFactory,
                config["FileService:ImagesByMasterIdsPath"],
                masterIds,
                jsonOptions);

            var productMap = products?.ToDictionary(p => p.Id) ?? new Dictionary<int, ProductDto>();
            var result = new BasketDetailDto
            {
                UserId = basket.UserId,
                StoreId = basket.StoreId,
                BasketMasterId = basket.BasketMasterId,
                DeliveryAddressId = basket.DeliveryAddressId,
                Items = basket.Items?
                    .Select(item =>
                    {
                        productMap.TryGetValue(item.ProductId, out var product);
                        var imageUrl = string.Empty;
                        if (product != null && imageMap.TryGetValue(product.ImageMasterId, out var urls) && urls.Length > 0)
                        {
                            imageUrl = urls[0];
                        }

                        return new BasketDetailItem
                        {
                            ProductId = item.ProductId,
                            ProductName = product?.Name ?? string.Empty,
                            ImageUrl = imageUrl,
                            Quantity = item.Quantity,
                            UnitType = item.UnitType,
                            ProductPrice = item.ProductPrice,
                            Weight = item.Weight
                        };
                    })
                    .ToList() ?? new List<BasketDetailItem>()
            };

            context.Response.StatusCode = StatusCodes.Status200OK;
            await context.Response.WriteAsJsonAsync(result, jsonOptions, context.RequestAborted);
        }
    }

    private static async Task AggregateOrders(HttpContext context)
    {
        if (!EnsureGetAndAuthenticated(context))
        {
            return;
        }

        var jsonOptions = CreateJsonOptions();
        var (httpClientFactory, config) = GetServices(context);

        var orgerManagementClient = httpClientFactory.CreateClient("OrderManagementService");
        var orderPath = config["OrderManagementService:OrderPath"];

        using var orderRequest = new HttpRequestMessage(HttpMethod.Get, orderPath);

        CopyAuthorizationHeader(context, orderRequest);

        using var orderResponse = await orgerManagementClient.SendAsync(orderRequest, context.RequestAborted);
        if (!orderResponse.IsSuccessStatusCode)
        {
            context.Response.StatusCode = (int)orderResponse.StatusCode;
            return;
        }

        var res = await orderResponse.Content.ReadAsStringAsync(context.RequestAborted);
        var orders = JsonSerializer.Deserialize<List<OrderDetail>>(res, jsonOptions);
        var storeIds = orders
            .Select(s => s.StoreId)
            .Distinct()
            .ToArray();

        var stores = await GetStoresProfile(
              context,
              httpClientFactory,
              config["EngagementService:GetStoresProfile"],
              storeIds,
              jsonOptions);

        var masterIds = stores
            .Select(s => s.ImageMasterId)
            .Where(id => id > 0)
            .Distinct()
            .ToArray();

        var imageMap = await LoadImagesByMasterIds(
            context,
            httpClientFactory,
            config["FileService:ImagesByMasterIdsPath"],
            masterIds,
            jsonOptions);

        var result = orders.Select(order => new OrderDetail
        {
            Id = order.Id,
            StoreId = order.StoreId,
            StoreName = stores.FirstOrDefault(x => x.UserId == order.StoreId).FirstName,
            StoreImageUrl = imageMap.TryGetValue(stores.FirstOrDefault(x => x.UserId == order.StoreId).ImageMasterId, out var urls)
                ? urls[0]
                : "",
            Tip = order.Tip,
            Total = order.Total,
            ServiceFee = order.ServiceFee,
            Subtotal = order.Subtotal,
            AssignedAtUtc = order.AssignedAtUtc,
            BasketMasterId = order.BasketMasterId,
            CustomerId = order.CustomerId,
            DeliveryFee = order.DeliveryFee,
            Notes = order.Notes,
            ShopperId = order.ShopperId,
            Status = order.Status,
        });

        context.Response.StatusCode = StatusCodes.Status200OK;
        await context.Response.WriteAsJsonAsync(result, jsonOptions, context.RequestAborted);
    }

    private static async Task AggregateCustomerOrders(HttpContext context)
    {
        if (!EnsureGetAndAuthenticated(context))
        {
            return;
        }

        var jsonOptions = CreateJsonOptions();
        var (httpClientFactory, config) = GetServices(context);

        var orgerManagementClient = httpClientFactory.CreateClient("OrderManagementService");
        var orderPath = config["OrderManagementService:CustomerOrderPath"];
        var customerId = context.Request.Query["customerId"].FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(customerId))
        {
            orderPath = QueryHelpers.AddQueryString(orderPath!, "customerId", customerId);
        }

        using var orderRequest = new HttpRequestMessage(HttpMethod.Get, orderPath);

        CopyAuthorizationHeader(context, orderRequest);

        using var orderResponse = await orgerManagementClient.SendAsync(orderRequest, context.RequestAborted);
        if (!orderResponse.IsSuccessStatusCode)
        {
            context.Response.StatusCode = (int)orderResponse.StatusCode;
            return;
        }

        var res = await orderResponse.Content.ReadAsStringAsync(context.RequestAborted);
        var orderResponseModel = JsonSerializer.Deserialize<OrderGetAllResponse>(res, jsonOptions) ?? new OrderGetAllResponse();
        var orders = orderResponseModel.Orders ?? new List<OrderDetail>();
        var storeIds = orders
            .Select(s => s.StoreId)
            .Distinct()
            .ToArray();

        var stores = await GetStoresProfile(
              context,
              httpClientFactory,
              config["EngagementService:GetStoresProfile"],
              storeIds,
              jsonOptions);

        var masterIds = stores
            .Select(s => s.ImageMasterId)
            .Where(id => id > 0)
            .Distinct()
            .ToArray();

        var imageMap = await LoadImagesByMasterIds(
            context,
            httpClientFactory,
            config["FileService:ImagesByMasterIdsPath"],
            masterIds,
            jsonOptions);

        var result = orders.Select(order => new OrderDetail
        {
            Id = order.Id,
            StoreId = order.StoreId,
            StoreName = stores.FirstOrDefault(x => x.UserId == order.StoreId).FirstName,
            StoreImageUrl = imageMap.TryGetValue(stores.FirstOrDefault(x => x.UserId == order.StoreId).ImageMasterId, out var urls)
                ? urls[0]
                : "",
            Tip = order.Tip,
            Total = order.Total,
            ServiceFee = order.ServiceFee,
            Subtotal = order.Subtotal,
            AssignedAtUtc = order.AssignedAtUtc,
            BasketMasterId = order.BasketMasterId,
            CustomerId = order.CustomerId,
            DeliveryFee = order.DeliveryFee,
            Notes = order.Notes,
            ShopperId = order.ShopperId,
            Status = order.Status,
        });

        var aggregateResponse = new OrderGetAllResponse
        {
            CustomerId = orderResponseModel.CustomerId,
            Orders = result.ToList()
        };

        context.Response.StatusCode = StatusCodes.Status200OK;
        await context.Response.WriteAsJsonAsync(aggregateResponse, jsonOptions, context.RequestAborted);
    }
    private static bool EnsureGetAndAuthenticated(HttpContext context)
    {
        if (!HttpMethods.IsGet(context.Request.Method))
        {
            context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            return false;
        }

        if (context.User?.Identity?.IsAuthenticated != true)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return false;
        }

        return true;
    }
    private static JsonSerializerOptions CreateJsonOptions() =>
        new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

    private static (IHttpClientFactory HttpClientFactory, IConfiguration Config) GetServices(HttpContext context)
    {
        var services = context.RequestServices;
        return (services.GetRequiredService<IHttpClientFactory>(), services.GetRequiredService<IConfiguration>());
    }

    private static void CopyAuthorizationHeader(HttpContext context, HttpRequestMessage request)
    {
        if (context.Request.Headers.TryGetValue(AuthorizationHeader, out var authHeader))
        {
            request.Headers.TryAddWithoutValidation(AuthorizationHeader, authHeader.ToString());
        }
    }

    private static async Task<Dictionary<int, string[]>> LoadImagesByMasterIds(
        HttpContext context,
        IHttpClientFactory httpClientFactory,
        string? filePath,
        int[]? masterIds,
        JsonSerializerOptions jsonOptions)
    {
        if (string.IsNullOrWhiteSpace(filePath) || masterIds?.Length > 0 != true)
        {
            return new Dictionary<int, string[]>();
        }

        var fileClient = httpClientFactory.CreateClient("FileService");
        using var fileRequest = new HttpRequestMessage(HttpMethod.Post, filePath)
        {
            Content = JsonContent.Create(new ImagesByIdsRequest(masterIds), options: jsonOptions)
        };

        CopyAuthorizationHeader(context, fileRequest);

        using var fileResponse = await fileClient.SendAsync(fileRequest, context.RequestAborted);
        if (!fileResponse.IsSuccessStatusCode)
        {
            return new Dictionary<int, string[]>();
        }

        var imageItems = await fileResponse.Content.ReadFromJsonAsync<List<ImageMasterDto>>(jsonOptions, context.RequestAborted)
                         ?? new List<ImageMasterDto>();
        return imageItems.ToDictionary(x => x.MasterId, x => x.ImageUrls);
    }

    private static async Task<List<StoreDto>> GetStoresProfile(
        HttpContext context,
        IHttpClientFactory httpClientFactory,
        string? storePath,
        string[]? storeIds,
        JsonSerializerOptions jsonOptions)
    {
        var engagementClient = httpClientFactory.CreateClient("EngagementService");
        using var storeRequest = new HttpRequestMessage(HttpMethod.Post, storePath)
        {
            Content = JsonContent.Create(storeIds, options: jsonOptions)
        };

        CopyAuthorizationHeader(context, storeRequest);

        using var storeResponse = await engagementClient.SendAsync(storeRequest, context.RequestAborted);
        if (!storeResponse.IsSuccessStatusCode)
        {
            return new List<StoreDto>();
        }

        return await storeResponse.Content.ReadFromJsonAsync<List<StoreDto>>(jsonOptions, context.RequestAborted);
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
