namespace Keycloak.Starter.Transactions.Configuration;

public sealed class TransactionsSettings
{
    public const string SectionName = "Keycloak";
    public string Authority { get; set; } = "";
    public string? MetadataAddress { get; set; }
}
