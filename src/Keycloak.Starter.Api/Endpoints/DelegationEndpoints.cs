using System.Net.Http.Headers;
using Keycloak.Starter.Api.Delegation;
using Microsoft.AspNetCore.Authentication;

namespace Keycloak.Starter.Api.Endpoints;

public static class DelegationEndpoints
{
    public static async Task<IResult> GetTransactionsAsync(
        HttpContext context, TokenExchangeClient exchange, IHttpClientFactory clients, CancellationToken cancellationToken)
    {
        try
        {
            var incoming = await context.GetTokenAsync("access_token");
            if (string.IsNullOrEmpty(incoming)) return Results.Unauthorized();
            var token = await exchange.ExchangeAsync(incoming, cancellationToken);
            using var client = clients.CreateClient("transactions");
            using var request = new HttpRequestMessage(HttpMethod.Get, "transactions");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await client.SendAsync(request, cancellationToken);
            // Downstream credentials belong to API A. Never challenge the browser for a downstream failure.
            if (!response.IsSuccessStatusCode)
                return Results.Problem(statusCode: 502, title: "Transactions API rejected the delegated request.");
            return Results.Content(await response.Content.ReadAsStringAsync(cancellationToken), "application/json");
        }
        catch (TokenExchangeException)
        {
            return Results.Problem(statusCode: 502, title: "User token exchange was rejected.");
        }
        catch (HttpRequestException)
        {
            return Results.Problem(statusCode: 503, title: "A downstream service is unavailable.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Results.Problem(statusCode: 504, title: "A downstream request timed out.");
        }
    }
}
