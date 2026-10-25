using System.Security.Claims;

namespace Keycloak.Starter.Api.Endpoints;

public static class ApiEndpointExtensions
{
    public static IEndpointRouteBuilder MapApiEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/health", () => "Ok!")
            .AllowAnonymous();

        endpoints.MapGet("/me", (HttpContext context) => Results.Ok(new { sub = context.User.FindFirst("sub")?.Value }))
            .WithSummary("Return the authenticated caller's subject")
            .RequireAuthorization();

        endpoints.MapGet("/reader", () => Results.Ok(new { message = "Reader access granted" }))
            .WithSummary("Requires the starter-api reader role")
            .RequireAuthorization("CanRead");

        endpoints.MapGet("/admin", () => Results.Ok(new { message = "Admin access granted" }))
            .WithSummary("Requires the starter-api admin role")
            .RequireAuthorization("CanAdmin");

        endpoints.MapGet("/accounts/transactions", DelegationEndpoints.GetTransactionsAsync)
            .WithSummary("Exchange the signed-in user's token and read their transactions")
            .RequireAuthorization("UserTransactions");

        endpoints.MapGet("/sync/status", (ClaimsPrincipal user) => Results.Ok(new
            { status = "ready", operation = "synchronization-check", sub = user.FindFirst("sub")?.Value }))
            .RequireAuthorization("WorkerSync");

        return endpoints;
    }
}
