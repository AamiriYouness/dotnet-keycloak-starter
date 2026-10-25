namespace Keycloak.Starter.Worker.Configuration;

public sealed class WorkerSettings
{
    public const string SectionName = "Worker";
    public string Authority { get; set; } = "";
    public string MetadataAddress { get; set; } = "";
    public string ClientId { get; set; } = "starter-worker";
    public string ClientSecret { get; set; } = "";
    public string ApiUrl { get; set; } = "";
    public int IntervalSeconds { get; set; } = 30;
}
