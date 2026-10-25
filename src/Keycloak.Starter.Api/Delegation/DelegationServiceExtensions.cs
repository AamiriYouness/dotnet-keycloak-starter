using Keycloak.Starter.Api.Configuration;
using Microsoft.Extensions.Options;

namespace Keycloak.Starter.Api.Delegation;

public static class DelegationServiceExtensions
{
    public static IServiceCollection AddTokenExchange(this IServiceCollection services)
    {
        services.AddTransient<TokenExchangeClient>();
        services.AddHttpClient("exchange", client => client.Timeout = TimeSpan.FromSeconds(15))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddHttpClient("transactions", (provider, client) =>
        {
            var settings = provider.GetRequiredService<IOptions<ExchangeSettings>>().Value;
            if (settings.Enabled) client.BaseAddress = new Uri(settings.TransactionsUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(15);
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        return services;
    }
}
