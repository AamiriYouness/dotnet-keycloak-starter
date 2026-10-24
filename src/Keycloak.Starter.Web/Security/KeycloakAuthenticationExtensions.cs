using Keycloak.Starter.Web.Configuration;
using Keycloak.Starter.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Keycloak.Starter.Web.Security;

public static class KeycloakAuthenticationExtensions
{
    public static IServiceCollection AddKeycloakWebAuthentication(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddSingleton<UserAccessTokenService>();
        services.AddHttpClient("keycloak");
        services.AddHttpClient<StarterApiClient>((provider, client) =>
        {
            var settings = provider.GetRequiredService<IOptions<ApiSettings>>().Value;
            client.BaseAddress = new Uri(settings.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(15);
        });

        services.AddAuthentication(options =>
        {
            options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
        })
        .AddCookie(options =>
        {
            options.Cookie.Name = "__Host-StarterWeb";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = false;
        })
        .AddOpenIdConnect();

        services.AddOptions<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme)
            .Configure<IOptions<KeycloakSettings>, IHostEnvironment>((options, configured, environment) =>
            {
                var settings = configured.Value;
                var authority = settings.Authority.TrimEnd('/');
                options.Authority = authority;
                if (!string.IsNullOrWhiteSpace(settings.MetadataAddress))
                {
                    options.MetadataAddress = settings.MetadataAddress;
                }
                options.ClientId = settings.ClientId;
                options.ClientSecret = settings.ClientSecret;
                options.ResponseType = OpenIdConnectResponseType.Code;
                options.ResponseMode = OpenIdConnectResponseMode.Query;
                options.UsePkce = true;
                options.RequireHttpsMetadata = !environment.IsDevelopment();
                options.MapInboundClaims = false;
                options.SaveTokens = true;
                options.UseTokenLifetime = false;
                options.TokenValidationParameters.NameClaimType = "preferred_username";
                options.Scope.Clear();
                options.Scope.Add("openid");
                options.Scope.Add("profile");
                options.Events.OnRedirectToIdentityProvider = context =>
                {
                    context.ProtocolMessage.IssuerAddress = authority + "/protocol/openid-connect/auth";
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToIdentityProviderForSignOut = context =>
                {
                    context.ProtocolMessage.IssuerAddress = authority + "/protocol/openid-connect/logout";
                    return Task.CompletedTask;
                };
                options.Events.OnTicketReceived = context =>
                {
                    context.Properties ??= new();
                    context.Properties.IsPersistent = true;
                    context.Properties.ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8);
                    return Task.CompletedTask;
                };
            });
        return services;
    }
}
