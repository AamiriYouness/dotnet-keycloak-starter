using System.Security.Claims;

namespace Keycloak.Starter.Transactions.Endpoints;

public static class TransactionsEndpoints
{
    public static IEndpointRouteBuilder MapTransactionsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
        endpoints.MapGet("/transactions", (ClaimsPrincipal user) => Results.Ok(new
        {
            sub = user.FindFirst("sub")?.Value,
            authorizedParty = user.FindFirst("azp")?.Value,
            operation = "read-transactions",
            transactions = new[] { new { id = "demo-001", amount = 42.50m, currency = "MAD" } }
        })).RequireAuthorization("ReadTransactions");
        return endpoints;
    }
}
