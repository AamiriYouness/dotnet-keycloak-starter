using Microsoft.Extensions.Options;

namespace Keycloak.Starter.Api.Configuration;

public static class ApiOptionsExtensions
{
    public static IServiceCollection AddApiOptions(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var development = environment.IsDevelopment();
        services.AddOptions<KeycloakSettings>().Bind(configuration.GetSection(KeycloakSettings.SectionName))
            .Validate(settings => IsAllowedUrl(settings.Authority, development),
                "Keycloak:Authority must be an absolute HTTPS URL (HTTP is allowed in Development).")
            .Validate(settings => !string.IsNullOrWhiteSpace(settings.Audience), "Keycloak:Audience must not be empty.")
            .Validate(settings => string.IsNullOrWhiteSpace(settings.MetadataAddress) || IsAllowedUrl(settings.MetadataAddress, development),
                "Keycloak:MetadataAddress must be an absolute HTTPS URL (HTTP is allowed in Development).")
            .ValidateOnStart();
        services.AddOptions<ExchangeSettings>().Bind(configuration.GetSection(ExchangeSettings.SectionName))
            .Validate<IOptions<KeycloakSettings>>((settings, keycloak) => !settings.Enabled ||
                (!string.IsNullOrWhiteSpace(settings.ClientId) && !string.IsNullOrWhiteSpace(settings.ClientSecret) &&
                 settings.Audience == "starter-transactions" && settings.ClientId == keycloak.Value.Audience),
                "TokenExchange requires the API client credentials and the Transactions audience.")
            .Validate(settings => !settings.Enabled || IsAllowedUrl(settings.TransactionsUrl, development),
                "TokenExchange:TransactionsUrl must be HTTPS (HTTP is allowed in Development).")
            .ValidateOnStart();
        return services;
    }

    private static bool IsAllowedUrl(string? value, bool development) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Scheme == "https" || (development && uri.Scheme == "http")) &&
        string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment);
}
