using System.ComponentModel.DataAnnotations;

namespace Keycloak.Starter.Web.Configuration;

public sealed class KeycloakSettings
{
    public const string SectionName = "Keycloak";
    [Required(ErrorMessage = "Keycloak:Authority is required.")]
    public string Authority { get; set; } = "";
    [Required(ErrorMessage = "Keycloak:ClientId is required.")]
    public string ClientId { get; set; } = "";
    [Required(ErrorMessage = "Keycloak:ClientSecret is required.")]
    public string ClientSecret { get; set; } = "";
    public string? MetadataAddress { get; set; }
}

public sealed class ApiSettings
{
    public const string SectionName = "Api";
    [Required(ErrorMessage = "Api:BaseUrl is required.")]
    public string BaseUrl { get; set; } = "";
}

public sealed class AuthenticationSettings
{
    public const string SectionName = "Authentication";
    [Required(ErrorMessage = "Authentication:StoragePath is required.")]
    public string StoragePath { get; set; } = "";
    [Range(1, 300, ErrorMessage = "Authentication:RefreshBeforeExpirySeconds must be between 1 and 300.")]
    public int RefreshBeforeExpirySeconds { get; set; } = 30;

    public string KeyDirectory => Path.Combine(StoragePath, "keys");
    public string TicketDirectory => Path.Combine(StoragePath, "tickets");
}

public sealed class DataProtectionSettings
{
    public const string SectionName = "DataProtection";
    public string? CertificatePath { get; set; }
    public string? CertificatePassword { get; set; }
}
