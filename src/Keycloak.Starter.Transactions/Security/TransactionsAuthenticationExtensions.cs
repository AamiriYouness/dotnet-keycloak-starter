using Keycloak.Starter.Authentication;
using Keycloak.Starter.Transactions.Configuration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;

namespace Keycloak.Starter.Transactions.Security;

public static class TransactionsAuthenticationExtensions
{
    public static IServiceCollection AddTransactionsAuthentication(this IServiceCollection services)
    {
        services.AddTransient<KeycloakJwtBearerEvents>();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<TransactionsSettings>, IHostEnvironment>((options, configured, environment) =>
            {
                var settings = configured.Value;
                options.Authority = settings.Authority;
                options.Audience = "starter-transactions";
                if (settings.MetadataAddress is not null) options.MetadataAddress = settings.MetadataAddress;
                options.RequireHttpsMetadata = !environment.IsDevelopment();
                options.MapInboundClaims = false;
                options.TokenValidationParameters.RoleClaimType = KeycloakJwtBearerEvents.RoleClaimType;
                options.EventsType = typeof(KeycloakJwtBearerEvents);
                options.IncludeErrorDetails = false;
                options.TokenValidationParameters.ClockSkew = TimeSpan.FromSeconds(30);
            });
        services.AddAuthorization(options => options.AddPolicy("ReadTransactions", policy =>
            policy.RequireAuthenticatedUser().RequireClaim("azp", "starter-api")
                .RequireRole("read-transactions")));
        return services;
    }

}
