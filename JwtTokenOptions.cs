namespace OrderDispatcher.Apigateway
{
    public class JwtTokenOptions
    {
        public string Issuer { get; set; } = default!;
        public string Audience { get; set; } = default!;
        public string Key { get; set; } = default!;
        public int ExpireInMinutes { get; set; }
    }
}
