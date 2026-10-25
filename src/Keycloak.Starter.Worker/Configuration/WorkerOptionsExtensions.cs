namespace Keycloak.Starter.Worker.Configuration;

public static class WorkerOptionsExtensions
{
    public static IServiceCollection AddWorkerOptions(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        bool ValidUrl(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            (uri.Scheme == "https" || (environment.IsDevelopment() && uri.Scheme == "http")) &&
            string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment);
        services.AddOptions<WorkerSettings>().Bind(configuration.GetSection(WorkerSettings.SectionName))
            .Validate(s => ValidUrl(s.Authority) && ValidUrl(s.ApiUrl) &&
                (string.IsNullOrEmpty(s.MetadataAddress) || ValidUrl(s.MetadataAddress)),
                "Worker authority, metadata and API URLs must use HTTPS (HTTP is allowed in Development).")
            .Validate(s => !string.IsNullOrWhiteSpace(s.ClientId) && !string.IsNullOrWhiteSpace(s.ClientSecret),
                "Worker client credentials are required.")
            .Validate(s => s.IntervalSeconds is >= 1 and <= 86400, "Worker interval must be between 1 and 86400 seconds.")
            .ValidateOnStart();
        return services;
    }
}
