using System.Text.Json.Serialization;

namespace Keycloak.Starter.Worker;

internal sealed class ClientCredentialsResponse
{
    [JsonPropertyName("access_token")]
    public required string AccessToken { get; init; }

    [JsonPropertyName("token_type")]
    public required string TokenType { get; init; }

    [JsonPropertyName("expires_in")]
    [JsonNumberHandling(JsonNumberHandling.Strict)]
    public required int ExpiresIn { get; init; }
}
