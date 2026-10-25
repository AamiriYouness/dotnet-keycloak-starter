using Keycloak.Starter.Api.Configuration;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;

namespace Keycloak.Starter.Api.Delegation;

// Contains no provider response body, token, or credentials, including in InnerException.
public sealed class TokenExchangeException : Exception
{
    public TokenExchangeException() : base("The authorization server rejected the token exchange.") { }
}

public sealed class TokenExchangeClient(
    IHttpClientFactory clients,
    IOptionsMonitor<JwtBearerOptions> bearerOptions,
    IOptions<ExchangeSettings> settings,
    IHostEnvironment environment)
{
    public const string AccessTokenType = "urn:ietf:params:oauth:token-type:access_token";

    public async Task<string> ExchangeAsync(string subjectToken, CancellationToken cancellationToken)
    {
        var options = settings.Value;
        if (!options.Enabled) throw new TokenExchangeException();
        var bearer = bearerOptions.Get(JwtBearerDefaults.AuthenticationScheme);
        var metadata = await bearer.ConfigurationManager!.GetConfigurationAsync(cancellationToken);
        // Metadata is an explicit trust boundary: never send credentials to an untrusted issuer.
        if (metadata.Issuer != bearer.Authority?.TrimEnd('/') ||
            !Uri.TryCreate(metadata.TokenEndpoint, UriKind.Absolute, out var endpoint) ||
            (endpoint.Scheme != "https" && !(environment.IsDevelopment() && endpoint.Scheme == "http")) ||
            !string.IsNullOrEmpty(endpoint.UserInfo) || !string.IsNullOrEmpty(endpoint.Query) || !string.IsNullOrEmpty(endpoint.Fragment))
            throw new TokenExchangeException();

        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:token-exchange",
            ["subject_token"] = subjectToken,
            ["subject_token_type"] = AccessTokenType,
            ["requested_token_type"] = AccessTokenType,
            ["audience"] = options.Audience,
            ["client_id"] = options.ClientId,
            ["client_secret"] = options.ClientSecret
        });
        using var client = clients.CreateClient("exchange");
        using var response = await client.PostAsync(endpoint, form, cancellationToken);
        if (!response.IsSuccessStatusCode) throw new TokenExchangeException();
        try
        {
            var token = await response.Content.ReadFromJsonAsync<TokenExchangeResponse>(cancellationToken);
            if (token is null || token.TokenType != "Bearer" || token.IssuedTokenType != AccessTokenType ||
                string.IsNullOrWhiteSpace(token.AccessToken))
                throw new TokenExchangeException();
            return token.AccessToken;
        }
        catch (JsonException)
        {
            throw new TokenExchangeException();
        }
    }
}
