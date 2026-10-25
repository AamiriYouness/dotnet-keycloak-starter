using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Keycloak.Starter.Web.Configuration;

public static class WebOptionsExtensions
{
    public static IServiceCollection AddWebOptions(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var development = environment.IsDevelopment();
        services.AddOptions<KeycloakSettings>()
            .Bind(configuration.GetSection(KeycloakSettings.SectionName))
            .ValidateDataAnnotations()
            .Validate(settings => IsAllowedUrl(settings.Authority, development),
                "Keycloak:Authority must be an absolute HTTP(S) URL, with HTTPS required outside Development.")
            .Validate(settings => string.IsNullOrWhiteSpace(settings.MetadataAddress)
                || IsAllowedUrl(settings.MetadataAddress, development),
                "Keycloak:MetadataAddress must be an absolute HTTP(S) URL, with HTTPS required outside Development.")
            .ValidateOnStart();

        services.AddOptions<ApiSettings>()
            .Bind(configuration.GetSection(ApiSettings.SectionName))
            .ValidateDataAnnotations()
            .Validate(settings => IsAllowedUrl(settings.BaseUrl, development),
                "Api:BaseUrl must be an absolute HTTP(S) URL, with HTTPS required outside Development.")
            .ValidateOnStart();

        services.AddOptions<AuthenticationSettings>()
            .Configure(settings => settings.StoragePath =
                Path.Combine(environment.ContentRootPath, ".local", "authentication"))
            .Bind(configuration.GetSection(AuthenticationSettings.SectionName))
            .ValidateDataAnnotations()
            .Validate(_ => development || !string.IsNullOrWhiteSpace(configuration["Authentication:StoragePath"]),
                "Authentication:StoragePath must be explicitly configured for persistent deployment storage outside Development.")
            .Validate(settings => !string.IsNullOrWhiteSpace(settings.StoragePath)
                && Path.IsPathFullyQualified(settings.StoragePath)
                && settings.StoragePath.IndexOfAny(Path.GetInvalidPathChars()) < 0,
                "Authentication:StoragePath must be an absolute valid filesystem path.")
            .ValidateOnStart();

        services.AddOptions<DataProtectionSettings>()
            .Bind(configuration.GetSection(DataProtectionSettings.SectionName))
            .Validate(settings => development || !string.IsNullOrWhiteSpace(settings.CertificatePath),
                "DataProtection:CertificatePath is required outside Development.")
            .Validate(settings => string.IsNullOrWhiteSpace(settings.CertificatePath)
                || File.Exists(settings.CertificatePath),
                "DataProtection:CertificatePath must reference an existing certificate file.")
            .Validate(settings => string.IsNullOrWhiteSpace(settings.CertificatePath) || HasPrivateKey(settings),
                "DataProtection:CertificatePath must contain a readable private key with the configured password.")
            .ValidateOnStart();
        return services;
    }

    private static bool HasPrivateKey(DataProtectionSettings settings)
    {
        try
        {
            using var certificate = X509CertificateLoader.LoadPkcs12FromFile(
                settings.CertificatePath!, settings.CertificatePassword);
            return certificate.HasPrivateKey;
        }
        catch (Exception exception) when (exception is CryptographicException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsAllowedUrl(string? value, bool development)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || (development && uri.Scheme == Uri.UriSchemeHttp))
            && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment);
    }
}
