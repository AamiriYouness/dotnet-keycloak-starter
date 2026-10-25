using System.Text.Json.Serialization;

namespace Keycloak.Starter.Worker;

internal sealed class OidcDiscoveryResponse
{
    [JsonPropertyName("issuer")]
    public required string Issuer { get; init; }

    [JsonPropertyName("token_endpoint")]
    public required string TokenEndpoint { get; init; }
}
