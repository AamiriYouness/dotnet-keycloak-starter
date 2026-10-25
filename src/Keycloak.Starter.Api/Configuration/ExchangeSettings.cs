namespace Keycloak.Starter.Api.Configuration;

public sealed class ExchangeSettings
{
    public const string SectionName = "TokenExchange";
    public bool Enabled { get; set; }
    public string ClientId { get; set; } = "starter-api";
    public string ClientSecret { get; set; } = "";
    public string Audience { get; set; } = "starter-transactions";
    public string TransactionsUrl { get; set; } = "";
}
