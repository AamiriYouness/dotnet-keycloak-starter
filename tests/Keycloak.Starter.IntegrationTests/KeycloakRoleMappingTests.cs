using System.Security.Claims;
using Keycloak.Starter.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;

namespace Keycloak.Starter.IntegrationTests;

public sealed class KeycloakRoleMappingTests
{
    private static TokenValidatedContext Context(string? resourceAccess, string audience = "starter-api", bool authenticated = true)
    {
        var identity = new ClaimsIdentity(authenticated ? "Bearer" : null, "sub", KeycloakJwtBearerEvents.RoleClaimType);
        if (resourceAccess is not null) identity.AddClaim(new Claim("resource_access", resourceAccess));
        return new TokenValidatedContext(new DefaultHttpContext(),
            new AuthenticationScheme("Bearer", null, typeof(JwtBearerHandler)),
            new JwtBearerOptions { Audience = audience }) { Principal = new ClaimsPrincipal(identity) };
    }

    [Theory]
    [InlineData("starter-api", "reader", "read-transactions")]
    [InlineData("starter-transactions", "read-transactions", "reader")]
    public async Task Only_the_current_resources_roles_are_mapped(string audience, string granted, string denied)
    {
        var context = Context("""
            {"starter-api":{"roles":["reader"]},"starter-transactions":{"roles":["read-transactions"]}}
            """, audience);
        await new KeycloakJwtBearerEvents().TokenValidated(context);
        Assert.True(context.Principal!.IsInRole(granted));
        Assert.False(context.Principal.IsInRole(denied));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-json")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"starter-api\":null}")]
    [InlineData("{\"starter-api\":{\"roles\":null}}")]
    [InlineData("{\"starter-api\":{\"roles\":\"admin\"}}")]
    [InlineData("{\"starter-api\":{\"roles\":[42]}}")]
    public async Task Absent_or_malformed_roles_grant_no_permissions(string? resourceAccess)
    {
        var context = Context(resourceAccess);
        await new KeycloakJwtBearerEvents().TokenValidated(context);
        Assert.Empty(context.Principal!.FindAll(KeycloakJwtBearerEvents.RoleClaimType));
    }

    [Fact]
    public async Task Realm_generic_and_incoming_application_role_claims_do_not_grant_api_roles()
    {
        var context = Context("{\"other-api\":{\"roles\":[\"admin\"]}}");
        var identity = (ClaimsIdentity)context.Principal!.Identity!;
        identity.AddClaim(new Claim(ClaimTypes.Role, "admin"));
        identity.AddClaim(new Claim("role", "admin"));
        identity.AddClaim(new Claim("realm_access", "{\"roles\":[\"admin\"]}"));
        identity.AddClaim(new Claim(KeycloakJwtBearerEvents.RoleClaimType, "admin"));
        await new KeycloakJwtBearerEvents().TokenValidated(context);
        Assert.False(context.Principal.IsInRole("admin"));
    }

    [Fact]
    public async Task Repeated_mapping_and_duplicate_roles_do_not_duplicate_claims()
    {
        var context = Context("{\"starter-api\":{\"roles\":[\"reader\",\"reader\",null,\"\",\" \"]}}");
        var events = new KeycloakJwtBearerEvents();
        await events.TokenValidated(context);
        await events.TokenValidated(context);
        Assert.Equal("reader", Assert.Single(context.Principal!.FindAll(KeycloakJwtBearerEvents.RoleClaimType)).Value);
    }

    [Fact]
    public async Task Unauthenticated_identity_is_not_enriched()
    {
        var context = Context("{\"starter-api\":{\"roles\":[\"admin\"]}}", authenticated: false);
        await new KeycloakJwtBearerEvents().TokenValidated(context);
        Assert.False(context.Principal!.IsInRole("admin"));
    }
}
