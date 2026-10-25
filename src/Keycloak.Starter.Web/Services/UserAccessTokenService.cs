using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Keycloak.Starter.Web.Configuration;
using Keycloak.Starter.Web.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;

namespace Keycloak.Starter.Web.Services;

public sealed class UserAccessTokenService(
    IHttpContextAccessor accessor,
    FileTicketStore tickets,
    IHttpClientFactory clients,
    IOptionsMonitor<OpenIdConnectOptions> oidc,
    IOptions<AuthenticationSettings> authenticationSettings) : IDisposable
{
    // Serialize refreshes within this single web instance, reloading the ticket inside the lock.
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public async Task<string?> GetAsync(CancellationToken cancellationToken)
    {
        var context = accessor.HttpContext ?? throw new InvalidOperationException("No active request.");
        var authentication = await context.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        var ticket = authentication.Ticket;
        if (ticket is null)
        {
            return null;
        }
        if (IsFresh(ticket.Properties))
        {
            return ticket.Properties.GetTokenValue("access_token");
        }
        if (!ticket.Properties.Items.TryGetValue(FileTicketStore.SessionKey, out var key) || key is null)
        {
            return null;
        }

        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            ticket = await tickets.RetrieveAsync(key);
            if (ticket is null)
            {
                return null;
            }
            if (IsFresh(ticket.Properties))
            {
                return ticket.Properties.GetTokenValue("access_token");
            }
            var refresh = ticket.Properties.GetTokenValue("refresh_token");
            if (string.IsNullOrEmpty(refresh))
            {
                await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return null;
            }
            var options = oidc.Get(OpenIdConnectDefaults.AuthenticationScheme);
            var discovery = options.Configuration
                ?? await options.ConfigurationManager!.GetConfigurationAsync(cancellationToken);
            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["client_id"] = options.ClientId!,
                ["client_secret"] = options.ClientSecret!,
                ["refresh_token"] = refresh
            });
            using var response = await clients.CreateClient("keycloak")
                .PostAsync(discovery.TokenEndpoint, form, cancellationToken);
            if (response.StatusCode is System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.Unauthorized)
            {
                await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return null;
            }
            response.EnsureSuccessStatusCode();

            RefreshTokenResponse? refreshed;
            try
            {
                refreshed = await response.Content.ReadFromJsonAsync<RefreshTokenResponse>(cancellationToken);
            }
            catch (JsonException)
            {
                // Keep provider content and token values out of exceptions and logs.
                throw new HttpRequestException("The authorization server returned an invalid refresh response.");
            }
            if (refreshed is null || string.IsNullOrWhiteSpace(refreshed.AccessToken) ||
                refreshed.TokenType != "Bearer" || refreshed.ExpiresIn <= 0)
                throw new HttpRequestException("The authorization server returned an invalid refresh response.");

            var tokens = ticket.Properties.GetTokens().ToDictionary(token => token.Name, token => token.Value);
            tokens["access_token"] = refreshed.AccessToken;
            tokens["token_type"] = refreshed.TokenType;
            if (refreshed.RefreshToken is { Length: > 0 }) tokens["refresh_token"] = refreshed.RefreshToken;
            if (refreshed.IdToken is { Length: > 0 }) tokens["id_token"] = refreshed.IdToken;
            tokens["expires_at"] = DateTimeOffset.UtcNow.AddSeconds(refreshed.ExpiresIn)
                .ToString("O", CultureInfo.InvariantCulture);
            ticket.Properties.StoreTokens(tokens.Select(pair => new AuthenticationToken
            {
                Name = pair.Key,
                Value = pair.Value
            }));
            await tickets.RenewAsync(key, ticket);
            return tokens["access_token"];
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private bool IsFresh(AuthenticationProperties properties)
    {
        var threshold = authenticationSettings.Value.RefreshBeforeExpirySeconds;
        return DateTimeOffset.TryParse(properties.GetTokenValue("expires_at"), CultureInfo.InvariantCulture,
                   DateTimeStyles.RoundtripKind, out var expiry)
               && expiry > DateTimeOffset.UtcNow.AddSeconds(threshold)
               && !string.IsNullOrEmpty(properties.GetTokenValue("access_token"));
    }

    public void Dispose() => _refreshLock.Dispose();
}
