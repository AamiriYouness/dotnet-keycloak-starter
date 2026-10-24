using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Keycloak.Starter.IntegrationTests;

public sealed class AuthenticationTests
{
    [Theory]
    [InlineData("/me")]
    [InlineData("/reader")]
    [InlineData("/admin")]
    public async Task Protected_endpoint_without_bearer_token_returns_unauthorized(string path)
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(response.Headers.WwwAuthenticate, header => header.Scheme == "Bearer");
    }

    [Fact]
    public async Task Health_is_public()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

        using var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Ok!", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }
}
