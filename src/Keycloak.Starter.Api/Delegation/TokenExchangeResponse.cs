using System.Text.Json.Serialization;

namespace Keycloak.Starter.Api.Delegation;

// A class intentionally avoids a record's generated ToString containing token values.
internal sealed class TokenExchangeResponse
{
    [JsonPropertyName("access_token")]
    public required string AccessToken { get; init; }

    [JsonPropertyName("token_type")]
    public required string TokenType { get; init; }

    [JsonPropertyName("issued_token_type")]
    public required string IssuedTokenType { get; init; }
}
