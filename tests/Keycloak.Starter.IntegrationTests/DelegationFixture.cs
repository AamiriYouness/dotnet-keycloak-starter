extern alias TransactionsApi;
extern alias WorkerApp;

using WorkerApp::Keycloak.Starter.Worker.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Authentication.JwtBearer;

using System.Text.Json.Nodes;
using System.Text.Json;
using TransactionsProgram = TransactionsApi::Program;
using WorkerApp::Keycloak.Starter.Worker;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Testcontainers.Keycloak;

namespace Keycloak.Starter.IntegrationTests;

public sealed class DelegationFixture : IAsyncLifetime
{
    private readonly string _realmPath = Path.Combine(Path.GetTempPath(), "starter-delegation-" + Guid.NewGuid().ToString("N") + ".json");
    private KeycloakContainer? _keycloak;
    public WebApplicationFactory<Program> Accounts { get; private set; } = null!;
    public WebApplicationFactory<TransactionsProgram> Transactions { get; private set; } = null!;
    public string Authority { get; private set; } = "";
    public string TokenEndpoint => Authority + "/protocol/openid-connect/token";

    public async ValueTask InitializeAsync()
    {
        try
        {
            var realm = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "TestData", "delegation-realm.json")))!;
            // Test-only password grant gives a real Keycloak user session without a browser in these API tests.
            // Production realm keeps password grants disabled. Browser tests cover code + PKCE separately.
            var web = realm["clients"]!.AsArray().Single(c => c!["clientId"]!.GetValue<string>() == "starter-web")!;
            web["directAccessGrantsEnabled"] = true;
            foreach (var id in new[] { "test-direct-transactions", "test-expired-transactions" })
            {
                var direct = realm["clients"]!.AsArray().Single(c => c!["clientId"]!.GetValue<string>() == "starter-worker")!.DeepClone();
                direct["clientId"] = id;
                direct["secret"] = "test-only-secret";
                direct["protocolMappers"]![0]!["config"]!["included.client.audience"] = "starter-transactions";
                if (id == "test-expired-transactions") direct["attributes"] = new JsonObject { ["access.token.lifespan"] = "1" };
                realm["clients"]!.AsArray().Add(direct);
                realm["clientScopeMappings"]!["starter-transactions"]!.AsArray().Add(new JsonObject
                { ["client"] = id, ["roles"] = new JsonArray("read-transactions") });
                realm["users"]!.AsArray().Add(new JsonObject
                {
                    ["username"] = "service-account-" + id, ["enabled"] = true, ["serviceAccountClientId"] = id,
                    ["clientRoles"] = new JsonObject { ["starter-transactions"] = new JsonArray("read-transactions") }
                });
            }
            await File.WriteAllTextAsync(_realmPath, realm.ToJsonString());
            _keycloak = new KeycloakBuilder("quay.io/keycloak/keycloak:26.8.1").WithRealm(_realmPath).Build();
            await _keycloak.StartAsync(TestContext.Current.CancellationToken);
            Authority = _keycloak.GetBaseAddress().TrimEnd('/') + "/realms/starter";
            Transactions = new WebApplicationFactory<TransactionsProgram>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.UseSetting("Keycloak:Authority", Authority);
                builder.ConfigureServices(services => services.Configure<JwtBearerOptions>(
                    JwtBearerDefaults.AuthenticationScheme, options => options.TokenValidationParameters.ClockSkew = TimeSpan.Zero));
            });
            _ = Transactions.Server;
            Accounts = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.UseSetting("Keycloak:Authority", Authority);
                builder.UseSetting("TokenExchange:Enabled", "true");
                builder.UseSetting("TokenExchange:ClientSecret", "local-demo-exchange-secret");
                builder.UseSetting("TokenExchange:TransactionsUrl", "https://transactions.test");
                builder.ConfigureServices(services => services.AddHttpClient("transactions")
                    .ConfigurePrimaryHttpMessageHandler(() => Transactions.Server.CreateHandler()));
            });
            _ = Accounts.Server;
        }
        catch { await DisposeAsync(); throw; }
    }

    public async Task<string> UserTokenAsync(string user = "demo-reader", string password = "local-reader-password")
    {
        using var response = await GrantAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "password", ["client_id"] = "starter-web", ["client_secret"] = "local-demo-web-secret",
            ["username"] = user, ["password"] = password
        });
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return json.RootElement.GetProperty("access_token").GetString()!;
    }

    public async Task<HttpResponseMessage> GrantAsync(Dictionary<string, string> fields)
    {
        using var http = new HttpClient();
        using var form = new FormUrlEncodedContent(fields);
        return await http.PostAsync(TokenEndpoint, form, TestContext.Current.CancellationToken);
    }

    public ServiceProvider CreateWorkerServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new TestEnvironment());
        services.AddSingleton(TimeProvider.System);
        services.Configure<WorkerSettings>(s =>
        {
            s.Authority = Authority;
            s.ClientId = "starter-worker";
            s.ClientSecret = "local-demo-worker-secret";
        });
        services.AddHttpClient("oauth");
        services.AddSingleton<ServiceTokenProvider>();
        return services.BuildServiceProvider();
    }

    public async ValueTask DisposeAsync()
    {
        if (Accounts is not null) await Accounts.DisposeAsync();
        if (Transactions is not null) await Transactions.DisposeAsync();
        if (_keycloak is not null) await _keycloak.DisposeAsync();
        if (File.Exists(_realmPath)) File.Delete(_realmPath);
    }
}
