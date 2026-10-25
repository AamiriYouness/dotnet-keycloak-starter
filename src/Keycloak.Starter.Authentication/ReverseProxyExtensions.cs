using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Keycloak.Starter.Authentication;

public static class ReverseProxyExtensions
{
    public static IServiceCollection AddTrustedReverseProxy(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ReverseProxySettings>().Bind(configuration.GetSection("ReverseProxy"))
            .Validate(settings => !settings.Enabled || settings.KnownProxies.Length > 0,
                "ReverseProxy:KnownProxies must explicitly identify trusted proxy IP addresses when enabled.")
            .Validate(settings => settings.KnownProxies.All(value => IPAddress.TryParse(value, out _)),
                "ReverseProxy:KnownProxies must contain IP addresses, not hostnames or network ranges.")
            .ValidateOnStart();
        services.AddOptions<ForwardedHeadersOptions>().Configure<IOptions<ReverseProxySettings>>((options, configured) =>
        {
            var settings = configured.Value;
            options.ForwardedHeaders = settings.Enabled
                ? ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto : ForwardedHeaders.None;
            options.ForwardLimit = 1;
            options.RequireHeaderSymmetry = true;
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
            foreach (var address in settings.KnownProxies) options.KnownProxies.Add(IPAddress.Parse(address));
        });
        return services;
    }
}
