using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Networks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.Keycloak;
using Testcontainers.PostgreSql;

namespace Keycloak.Starter.IntegrationTests;

public sealed class KeycloakFixture : IAsyncLifetime
{
    public const string Realm = "starter-integration-tests";
    private const string ClientSecret = "integration-test-secret";

    private readonly INetwork _network = new NetworkBuilder().Build();
    private readonly PostgreSqlContainer _postgres;
    private readonly KeycloakContainer _keycloak;
    private readonly HttpClient _tokenClient = new();
    private WebApplicationFactory<Program>? _factory;

    public KeycloakFixture()
    {
        _postgres = new PostgreSqlBuilder("postgres:17.11")
            .WithNetwork(_network)
            .WithNetworkAliases("keycloak-db")
            .WithDatabase("keycloak")
            .WithUsername("keycloak")
            .WithPassword("integration-test-db-password")
            .Build();

        _keycloak = new KeycloakBuilder("quay.io/keycloak/keycloak:26.8.1")
            .WithNetwork(_network)
            .WithEnvironment("KC_DB", "postgres")
            .WithEnvironment("KC_DB_URL", "jdbc:postgresql://keycloak-db:5432/keycloak")
            .WithEnvironment("KC_DB_USERNAME", "keycloak")
            .WithEnvironment("KC_DB_PASSWORD", "integration-test-db-password")
            .WithRealm(Path.Combine(AppContext.BaseDirectory, "TestData", "realm.json"))
            .WithResourceMapping(
                Path.Combine(AppContext.BaseDirectory, "TestData", "untrusted-integration-tests-realm.json"),
                "/opt/keycloak/data/import/")
            .Build();
    }

    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        try
        {
            await _network.CreateAsync(cancellationToken);
            await _postgres.StartAsync(cancellationToken);
            await _keycloak.StartAsync(cancellationToken);

            var authority = $"{_keycloak.GetBaseAddress().TrimEnd('/')}/realms/{Realm}";
            _factory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder.UseEnvironment("Development");
                    builder.ConfigureServices(services =>
                        services.Configure<JwtBearerOptions>(
                            JwtBearerDefaults.AuthenticationScheme,
                            options =>
                            {
                                options.Authority = authority;
                                options.Audience = "starter-api";
                                // Test the expiration boundary without waiting for the default grace period.
                                options.TokenValidationParameters.ClockSkew = TimeSpan.Zero;
                            }));
                });
        }
        catch
        {
            // xUnit doesn't dispose fixtures whose initialization throws.
            await DisposeAsync();
            throw;
        }
    }

    public HttpClient CreateClient()
    {
        return (_factory ?? throw new InvalidOperationException("Fixture has not started."))
            .CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false
            });
    }

    public async Task<string> GetAccessTokenAsync(
        string clientId,
        CancellationToken cancellationToken,
        string realm = Realm)
    {
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_secret"] = ClientSecret
        });
        var endpoint = $"{_keycloak.GetBaseAddress().TrimEnd('/')}/realms/{realm}/protocol/openid-connect/token";
        using var response = await _tokenClient.PostAsync(endpoint, form, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));
        return document.RootElement.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("Keycloak returned no access token.");
    }

    public async ValueTask DisposeAsync()
    {
        _tokenClient.Dispose();
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        await _keycloak.DisposeAsync();
        await _postgres.DisposeAsync();
        await _network.DisposeAsync();
    }
}
