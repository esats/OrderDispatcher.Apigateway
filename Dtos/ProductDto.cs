namespace OrderDispatcher.Apigateway.Dtos;

public sealed class ProductDto
{
    public int Id { get; init; }
    public string Name { get; init; }
    public string Description { get; init; }
    public decimal Price { get; init; }
    public int Stock { get; init; }
    public int BrandId { get; init; }
    public int CategoryId { get; init; }
    public int ImageMasterId { get; init; }
    public int Order { get; init; }
    public string[] ImageUrls { get; set; } = Array.Empty<string>();
}
