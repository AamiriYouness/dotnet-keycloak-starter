using System.Net;
using Keycloak.Starter.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Keycloak.Starter.IntegrationTests;

public sealed class ReverseProxyTests
{
    [Theory]
    [InlineData("10.0.0.2", true, "https|203.0.113.7|api.example")]
    [InlineData("10.0.0.9", true, "http|10.0.0.9|api.example")]
    [InlineData("10.0.0.2", false, "http|10.0.0.2|api.example")]
    public async Task Forwarded_headers_are_accepted_only_from_the_explicit_proxy(string remoteAddress, bool enabled, string expected)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["ReverseProxy:Enabled"] = enabled.ToString(), ["ReverseProxy:KnownProxies:0"] = "10.0.0.2" }).Build();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddTrustedReverseProxy(configuration);
        await using var app = builder.Build();
        app.Use((context, next) => { context.Connection.RemoteIpAddress = IPAddress.Parse(remoteAddress); return next(); });
        app.UseForwardedHeaders();
        app.Run(context => context.Response.WriteAsync($"{context.Request.Scheme}|{context.Connection.RemoteIpAddress}|{context.Request.Host}"));
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://api.example/");
        request.Headers.Add("X-Forwarded-For", "203.0.113.7");
        request.Headers.Add("X-Forwarded-Proto", "https");
        request.Headers.Add("X-Forwarded-Host", "attacker.invalid");
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(expected, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("proxy.example")]
    [InlineData("10.0.0.0/8")]
    public void Enabled_proxy_requires_explicit_ip_addresses(string? address)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["ReverseProxy:Enabled"] = "true", ["ReverseProxy:KnownProxies:0"] = address }).Build();
        var services = new ServiceCollection();
        services.AddTrustedReverseProxy(configuration);
        using var provider = services.BuildServiceProvider();
        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<ReverseProxySettings>>().Value);
    }
}
