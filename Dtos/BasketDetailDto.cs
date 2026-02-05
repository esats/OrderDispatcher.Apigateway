namespace OrderDispatcher.Apigateway.Dtos
{
    public class BasketDetailDto
    {
        public string UserId { get; set; }
        public string StoreId { get; set; }
        public int BasketMasterId { get; set; }
        public int DeliveryAddressId { get; set; }
        public List<BasketDetailItem> Items { get; set; }
    }

    public class BasketDetailItem
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; }
        public string ImageUrl { get; set; }
        public int Quantity { get; set; }
        public int UnitType { get; set; }
        public decimal Weight { get; set; } = 0;
    }
}
