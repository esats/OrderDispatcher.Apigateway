namespace OrderDispatcher.Apigateway.Dtos;

public class ImageMasterDto
{
    public int MasterId { get; set; }
    public string[] ImageUrls { get; set; } = Array.Empty<string>();
}

public record ImagesByIdsRequest(IReadOnlyList<int> MasterIds);
