using System.Text.Json.Serialization;

namespace Keycloak.Starter.Authentication;

internal sealed class KeycloakClientRoles
{
    [JsonPropertyName("roles")]
    public string?[]? Roles { get; init; }
}
