using Keycloak.Starter.Worker.Configuration;
using System.Text.Json;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace Keycloak.Starter.Worker;

public sealed class TokenAcquisitionException : Exception
{
    public TokenAcquisitionException() : base("Service token acquisition failed.") { }
}

// Singleton: the semaphore and the cache are shared across all handler instances.
public sealed class ServiceTokenProvider(
    IHttpClientFactory clients, IOptions<WorkerSettings> settings, IHostEnvironment environment, TimeProvider clock) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _token;
    private DateTimeOffset _renewAt;
    private Uri? _endpoint;

    public async Task<string> GetAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_token is not null && clock.GetUtcNow() < _renewAt) return _token;
            var options = settings.Value;
            using var client = clients.CreateClient("oauth");
            if (_endpoint is null)
            {
                var address = string.IsNullOrWhiteSpace(options.MetadataAddress)
                    ? options.Authority.TrimEnd('/') + "/.well-known/openid-configuration" : options.MetadataAddress;
                using var metadataResponse = await client.GetAsync(address, cancellationToken);
                if (!metadataResponse.IsSuccessStatusCode) throw new TokenAcquisitionException();
                var metadata = await metadataResponse.Content.ReadFromJsonAsync<OidcDiscoveryResponse>(cancellationToken);
                if (metadata is null || metadata.Issuer != options.Authority.TrimEnd('/') ||
                    !Uri.TryCreate(metadata.TokenEndpoint, UriKind.Absolute, out var endpoint) ||
                    (endpoint.Scheme != "https" && !(environment.IsDevelopment() && endpoint.Scheme == "http")) ||
                    !string.IsNullOrEmpty(endpoint.UserInfo) || !string.IsNullOrEmpty(endpoint.Query) || !string.IsNullOrEmpty(endpoint.Fragment))
                    throw new TokenAcquisitionException();
                _endpoint = endpoint;
            }
            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = options.ClientId,
                ["client_secret"] = options.ClientSecret
            });
            var requestedAt = clock.GetUtcNow();
            using var response = await client.PostAsync(_endpoint, form, cancellationToken);
            if (!response.IsSuccessStatusCode) throw new TokenAcquisitionException();
            var token = await response.Content.ReadFromJsonAsync<ClientCredentialsResponse>(cancellationToken);
            if (token is null || token.TokenType != "Bearer" || token.ExpiresIn <= 0 || token.ExpiresIn > 86400 ||
                string.IsNullOrWhiteSpace(token.AccessToken))
                throw new TokenAcquisitionException();
            var lifetime = token.ExpiresIn;
            // Short-lived tokens keep 90% of their lifetime; long-lived tokens renew 30s early.
            _renewAt = requestedAt.AddSeconds(lifetime - Math.Min(30, lifetime * 0.1));
            if (_renewAt <= clock.GetUtcNow()) throw new TokenAcquisitionException();
            _token = token.AccessToken;
            return token.AccessToken;
        }
        catch (JsonException)
        {
            throw new TokenAcquisitionException();
        }
        finally { _gate.Release(); }
    }

    public void Dispose() => _gate.Dispose();
}
