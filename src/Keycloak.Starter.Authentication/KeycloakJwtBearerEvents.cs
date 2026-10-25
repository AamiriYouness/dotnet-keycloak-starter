using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace Keycloak.Starter.Authentication;

// Used only by the JWT bearer scheme, after signature/issuer/audience/lifetime validation.
public sealed class KeycloakJwtBearerEvents : JwtBearerEvents
{
    public const string RoleClaimType = "urn:keycloak-starter:api-role";

    public override Task TokenValidated(TokenValidatedContext context)
    {
        if (context.Principal?.Identity is not ClaimsIdentity { IsAuthenticated: true } identity)
            return Task.CompletedTask;

        // This claim type belongs to the application. Only the configured resource's roles may populate it.
        // Rebuilding also makes repeated mapping idempotent and ignores similarly named token claims.
        foreach (var claim in identity.FindAll(RoleClaimType).ToArray()) identity.RemoveClaim(claim);

        var audience = context.Options.Audience;
        var resourceAccess = identity.FindFirst("resource_access")?.Value;
        if (string.IsNullOrWhiteSpace(audience) || string.IsNullOrWhiteSpace(resourceAccess))
            return Task.CompletedTask;

        try
        {
            var resources = JsonSerializer.Deserialize<Dictionary<string, KeycloakClientRoles?>>(resourceAccess);
            if (resources is null || !resources.TryGetValue(audience, out var resource) || resource?.Roles is null)
                return Task.CompletedTask;

            foreach (var role in resource.Roles.Where(role => !string.IsNullOrWhiteSpace(role)).Distinct(StringComparer.Ordinal))
                identity.AddClaim(new Claim(RoleClaimType, role!, ClaimValueTypes.String, identity.FindFirst("iss")?.Value ?? ClaimsIdentity.DefaultIssuer));
        }
        catch (JsonException)
        {
            // Malformed resource roles grant no API permissions. Never log token content.
        }
        return Task.CompletedTask;
    }
}
