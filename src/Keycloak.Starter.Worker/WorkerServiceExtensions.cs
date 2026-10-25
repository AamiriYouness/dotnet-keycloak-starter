using Keycloak.Starter.Worker.Configuration;
using Microsoft.Extensions.Options;

namespace Keycloak.Starter.Worker;

public static class WorkerServiceExtensions
{
    public static IServiceCollection AddWorkerAuthentication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ServiceTokenProvider>();
        services.AddTransient<ServiceBearerHandler>();
        services.AddHttpClient("oauth", client => client.Timeout = TimeSpan.FromSeconds(15))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddHttpClient("sync", (provider, client) =>
        {
            client.BaseAddress = new Uri(provider.GetRequiredService<IOptions<WorkerSettings>>().Value.ApiUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(15);
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
            .AddHttpMessageHandler<ServiceBearerHandler>();
        return services;
    }
}
