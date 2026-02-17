public class OrderDetail
{
    public long Id { get; set; }
    public string StoreId { get; set; }
    public string StoreName { get; set; }
    public string StoreImageUrl { get; set; }
    public string CustomerId { get; set; }
    public string? ShopperId { get; set; }
    public int BasketMasterId { get; set; }
    public DateTime? AssignedAtUtc { get; set; }
    public byte Status { get; set; }
    public decimal? Subtotal { get; set; }
    public decimal? DeliveryFee { get; set; }
    public decimal? ServiceFee { get; set; }
    public decimal? Tip { get; set; }
    public decimal? Total { get; set; }
    public string? Notes { get; set; }
}

public class OrderGetAllResponse
{
    public string CustomerId { get; set; }
    public List<OrderDetail> Orders { get; set; } = new();
}
