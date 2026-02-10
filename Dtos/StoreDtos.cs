namespace OrderDispatcher.Apigateway.Dtos;

public sealed class StoreDto
{
    public string UserId { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Email { get; set; }
    public string? UserName { get; set; }
    public int ImageMasterId { get; set; }
}

public sealed class StoreListResponseDto
{
    public bool IsSuccess { get; set; }
    public List<StoreDto> Value { get; set; } = new();
    public string? Message { get; set; }
}

public sealed class StoreWithImagesDto
{
    public string StoreId { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Email { get; set; }
    public string? UserName { get; set; }
    public int ImageMasterId { get; set; }
    public string[] ImageUrls { get; set; } = Array.Empty<string>();
}


public record StoresByIdsRequest(IReadOnlyList<string> StoreIds);
