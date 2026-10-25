extern alias WorkerApp;

using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Keycloak.Starter.Api.Delegation;
using WorkerApp::Keycloak.Starter.Worker;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Keycloak.Starter.IntegrationTests;

[Trait("Category", "Containers")]
public sealed class DelegationTests(DelegationFixture fixture) : IClassFixture<DelegationFixture>
{
    private static HttpClient Client<T>(WebApplicationFactory<T> factory, string token) where T : class
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task User_can_call_accounts_but_original_token_is_rejected_by_transactions()
    {
        var token = await fixture.UserTokenAsync();
        Assert.DoesNotContain("starter-transactions", new JsonWebToken(token).Audiences);
        using var accounts = Client(fixture.Accounts, token);
        using var me = await accounts.GetAsync("/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        using var transactions = Client(fixture.Transactions, token);
        using var rejected = await transactions.GetAsync("/transactions", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
    }

    [Fact]
    public async Task Exchange_preserves_subject_and_restricts_audience_and_permissions()
    {
        var original = await fixture.UserTokenAsync();
        var exchange = fixture.Accounts.Services.GetRequiredService<TokenExchangeClient>();
        var token = await exchange.ExchangeAsync(original, TestContext.Current.CancellationToken);
        var jwt = new JsonWebToken(token);
        Assert.Equal(new JsonWebToken(original).Subject, jwt.Subject);
        Assert.Equal("starter-transactions", Assert.Single(jwt.Audiences));
        Assert.Equal("starter-api", jwt.GetClaim("azp").Value);
        using var transactions = Client(fixture.Transactions, token);
        using var response = await transactions.GetAsync("/transactions", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(jwt.Subject, body.RootElement.GetProperty("sub").GetString());
        Assert.Equal("read-transactions", body.RootElement.GetProperty("operation").GetString());
        using var accounts = Client(fixture.Accounts, token);
        using var wrongAudience = await accounts.GetAsync("/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongAudience.StatusCode);
    }

    [Fact]
    public async Task Accounts_calls_transactions_using_exchange()
    {
        using var client = Client(fixture.Accounts, await fixture.UserTokenAsync());
        using var response = await client.GetAsync("/accounts/transactions", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("read-transactions", body);
        Assert.DoesNotContain("access_token", body);
    }

    [Theory]
    [InlineData("starter-web", "local-demo-web-secret", "starter-transactions")]
    [InlineData("starter-worker", "local-demo-worker-secret", "starter-transactions")]
    [InlineData("starter-api", "local-demo-exchange-secret", "starter-admin")]
    [InlineData("starter-api", "wrong-secret", "starter-transactions")]
    public async Task Unauthorized_requester_or_target_is_rejected(string clientId, string secret, string audience)
    {
        using var response = await fixture.GrantAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:token-exchange",
            ["subject_token"] = await fixture.UserTokenAsync(),
            ["subject_token_type"] = TokenExchangeClient.AccessTokenType,
            ["requested_token_type"] = TokenExchangeClient.AccessTokenType,
            ["client_id"] = clientId, ["client_secret"] = secret, ["audience"] = audience
        });
        Assert.False(response.IsSuccessStatusCode);
        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.BadRequest, HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden });
    }

    [Theory]
    [InlineData("test-direct-transactions", false)]
    [InlineData("test-expired-transactions", true)]
    public async Task Transactions_rejects_wrong_authorized_party_and_expired_tokens(string clientId, bool expire)
    {
        using var grant = await fixture.GrantAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials", ["client_id"] = clientId, ["client_secret"] = "test-only-secret"
        });
        grant.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await grant.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var token = body.RootElement.GetProperty("access_token").GetString()!;
        if (expire)
        {
            var delay = new JsonWebToken(token).ValidTo - DateTime.UtcNow + TimeSpan.FromMilliseconds(200);
            if (delay > TimeSpan.Zero) await Task.Delay(delay, TestContext.Current.CancellationToken);
        }
        using var client = Client(fixture.Transactions, token);
        using var response = await client.GetAsync("/transactions", TestContext.Current.CancellationToken);
        Assert.Equal(expire ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task User_without_permission_cannot_request_delegation()
    {
        using var client = Client(fixture.Accounts, await fixture.UserTokenAsync("demo-unprivileged", "local-user-password"));
        using var response = await client.GetAsync("/accounts/transactions", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Worker_obtains_and_reuses_service_token_and_is_limited_to_sync()
    {
        await using var services = fixture.CreateWorkerServices();
        var tokens = services.GetRequiredService<ServiceTokenProvider>();
        var token = await tokens.GetAsync(TestContext.Current.CancellationToken);
        Assert.Equal(token, await tokens.GetAsync(TestContext.Current.CancellationToken));
        using var accounts = Client(fixture.Accounts, token);
        using var sync = await accounts.GetAsync("/sync/status", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, sync.StatusCode);
        foreach (var path in new[] { "/accounts/transactions", "/reader", "/admin" })
        {
            using var forbidden = await accounts.GetAsync(path, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }
        using var transactions = Client(fixture.Transactions, token);
        using var wrongAudience = await transactions.GetAsync("/transactions", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongAudience.StatusCode);
    }

    [Fact]
    public async Task User_cannot_use_worker_endpoint()
    {
        using var accounts = Client(fixture.Accounts, await fixture.UserTokenAsync());
        using var response = await accounts.GetAsync("/sync/status", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("/accounts/transactions")]
    [InlineData("/sync/status")]
    public async Task Invalid_token_is_rejected(string path)
    {
        using var client = Client(fixture.Accounts, "invalid-token");
        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
