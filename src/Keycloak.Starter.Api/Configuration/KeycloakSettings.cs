namespace Keycloak.Starter.Api.Configuration;

public sealed class KeycloakSettings
{
    public const string SectionName = "Keycloak";
    public string Authority { get; set; } = "";
    public string Audience { get; set; } = "";
    public string? MetadataAddress { get; set; }
}
