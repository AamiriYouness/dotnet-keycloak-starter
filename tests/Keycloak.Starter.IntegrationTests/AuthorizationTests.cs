using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Keycloak.Starter.IntegrationTests;

[Trait("Category", "Containers")]
public sealed class AuthorizationTests(KeycloakFixture fixture) : IClassFixture<KeycloakFixture>
{
    [Theory]
    [InlineData("test-reader", "/reader", HttpStatusCode.OK)]
    [InlineData("test-reader", "/admin", HttpStatusCode.Forbidden)]
    [InlineData("test-admin", "/admin", HttpStatusCode.OK)]
    [InlineData("test-admin", "/reader", HttpStatusCode.Forbidden)]
    [InlineData("test-unprivileged", "/reader", HttpStatusCode.Forbidden)]
    [InlineData("test-unprivileged", "/admin", HttpStatusCode.Forbidden)]
    [InlineData("test-foreign-roles", "/reader", HttpStatusCode.Forbidden)]
    [InlineData("test-foreign-roles", "/admin", HttpStatusCode.Forbidden)]
    public async Task Endpoint_enforces_its_api_role(
        string clientId, string path, HttpStatusCode expectedStatus)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await fixture.GetAccessTokenAsync(clientId, cancellationToken);
        using var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.GetAsync(path, cancellationToken);

        Assert.Equal(expectedStatus, response.StatusCode);
    }

    [Theory]
    [InlineData("test-reader")]
    [InlineData("test-admin")]
    [InlineData("test-unprivileged")]
    public async Task Me_returns_the_subject_from_the_validated_token(string clientId)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await fixture.GetAccessTokenAsync(clientId, cancellationToken);
        using var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.GetAsync("/me", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(new JsonWebToken(token).Subject, body.RootElement.GetProperty("sub").GetString());
    }

    [Fact]
    public async Task Token_for_another_audience_is_rejected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await fixture.GetAccessTokenAsync("test-wrong-audience", cancellationToken);

        await AssertUnauthorizedAsync(token, cancellationToken);
    }

    [Fact]
    public async Task Token_from_another_realm_is_rejected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await fixture.GetAccessTokenAsync(
            "test-untrusted", cancellationToken, "untrusted-integration-tests");

        await AssertUnauthorizedAsync(token, cancellationToken);
    }

    [Fact]
    public async Task Tampered_token_is_rejected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await fixture.GetAccessTokenAsync("test-reader", cancellationToken);
        var parts = token.Split('.');
        var payload = JsonNode.Parse(Base64UrlEncoder.Decode(parts[1]))!;
        payload["sub"] = "tampered-subject";
        parts[1] = Base64UrlEncoder.Encode(payload.ToJsonString());

        await AssertUnauthorizedAsync(string.Join('.', parts), cancellationToken);
    }

    [Fact]
    public async Task Expired_token_is_rejected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await fixture.GetAccessTokenAsync("test-short-lived", cancellationToken);
        var delay = new JsonWebToken(token).ValidTo - DateTime.UtcNow + TimeSpan.FromMilliseconds(200);
        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, cancellationToken);
        }

        await AssertUnauthorizedAsync(token, cancellationToken);
    }

    [Fact]
    public async Task Malformed_resource_access_grants_no_role()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await fixture.GetAccessTokenAsync("test-malformed-roles", cancellationToken);
        Assert.Equal("not-json", new JsonWebToken(token).GetClaim("resource_access").Value);
        using var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.GetAsync("/reader", cancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Malformed_token_is_rejected()
    {
        await AssertUnauthorizedAsync("not-a-jwt", TestContext.Current.CancellationToken);
    }

    private async Task AssertUnauthorizedAsync(string token, CancellationToken cancellationToken)
    {
        using var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.GetAsync("/me", cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(response.Headers.WwwAuthenticate, header => header.Scheme == "Bearer");
    }
}
