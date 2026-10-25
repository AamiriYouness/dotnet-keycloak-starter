using Keycloak.Starter.Authentication;
using Keycloak.Starter.Api.Configuration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;

namespace Keycloak.Starter.Api.Security;

public static class KeycloakAuthenticationExtensions
{
    public static IServiceCollection AddKeycloakApiAuthentication(this IServiceCollection services)
    {
        services.AddTransient<KeycloakJwtBearerEvents>();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<KeycloakSettings>, IHostEnvironment>((options, configured, environment) =>
            {
                var settings = configured.Value;
                var authority = settings.Authority;
                var audience = settings.Audience;
                var metadataAddress = settings.MetadataAddress;
                options.Authority = authority;
                options.Audience = audience;
                if (!string.IsNullOrWhiteSpace(metadataAddress))
                {
                    options.MetadataAddress = metadataAddress;
                }
                options.RequireHttpsMetadata = !environment.IsDevelopment();
                options.IncludeErrorDetails = environment.IsDevelopment();
                options.MapInboundClaims = false;
                options.TokenValidationParameters.RoleClaimType = KeycloakJwtBearerEvents.RoleClaimType;
                options.EventsType = typeof(KeycloakJwtBearerEvents);
            });

        services.AddAuthorization(options =>
        {
            // starter-web has no service account or password grant in the imported realm.
            options.AddPolicy("UserTransactions", policy => policy.RequireAuthenticatedUser()
                .RequireClaim("azp", "starter-web").RequireRole("reader"));
            options.AddPolicy("WorkerSync", policy => policy.RequireAuthenticatedUser()
                .RequireClaim("azp", "starter-worker").RequireRole("sync"));
            options.AddPolicy("CanRead", policy => policy.RequireAuthenticatedUser().RequireRole("reader"));
            options.AddPolicy("CanAdmin", policy => policy.RequireAuthenticatedUser().RequireRole("admin"));
        });

        return services;
    }
}
