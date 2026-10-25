namespace Keycloak.Starter.Transactions.Configuration;

public static class TransactionsOptionsExtensions
{
    public static IServiceCollection AddTransactionsOptions(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var development = environment.IsDevelopment();
        services.AddOptions<TransactionsSettings>().Bind(configuration.GetSection(TransactionsSettings.SectionName))
            .Validate(settings => IsAllowedUrl(settings.Authority, development),
                "Keycloak:Authority must use HTTPS (HTTP is allowed in Development).")
            .Validate(settings => settings.MetadataAddress is null || IsAllowedUrl(settings.MetadataAddress, development),
                "Keycloak:MetadataAddress must use HTTPS (HTTP is allowed in Development).")
            .ValidateOnStart();
        return services;
    }

    private static bool IsAllowedUrl(string? value, bool development) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Scheme == "https" || (development && uri.Scheme == "http")) &&
        string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment);
}
