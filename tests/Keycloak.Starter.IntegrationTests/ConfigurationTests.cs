using System.Net;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Keycloak.Starter.IntegrationTests;

public sealed class ConfigurationTests
{
    [Theory]
    [InlineData("Keycloak:Authority", "http://keycloak.example/realms/starter", "Keycloak:Authority")]
    [InlineData("Keycloak:Authority", "not-a-url", "Keycloak:Authority")]
    [InlineData("Keycloak:Audience", " ", "Keycloak:Audience")]
    [InlineData("Keycloak:MetadataAddress", "http://keycloak.example/discovery", "Keycloak:MetadataAddress")]
    public void Production_rejects_insecure_or_invalid_settings(
        string key, string value, string expectedMessage)
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.UseSetting("Keycloak:Authority", "https://keycloak.example/realms/starter");
                builder.UseSetting("Keycloak:Audience", "starter-api");
                builder.UseSetting(key, value);
            });

        var exception = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());

        Assert.Contains(expectedMessage, exception.Message);
    }

    [Theory]
    [InlineData("Development", HttpStatusCode.OK)]
    [InlineData("Production", HttpStatusCode.NotFound)]
    public async Task OpenApi_is_only_exposed_in_development(string environment, HttpStatusCode expectedStatus)
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(environment);
                builder.UseSetting("Keycloak:Authority", "https://keycloak.example/realms/starter");
                builder.UseSetting("Keycloak:Audience", "starter-api");
            });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
        var cancellationToken = TestContext.Current.CancellationToken;

        using var health = await client.GetAsync("/health", cancellationToken);
        using var openApi = await client.GetAsync("/openapi/v1.json", cancellationToken);
        using var swaggerUi = await client.GetAsync("/swagger/index.html", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(expectedStatus, openApi.StatusCode);
        Assert.Equal(expectedStatus, swaggerUi.StatusCode);
    }
}
