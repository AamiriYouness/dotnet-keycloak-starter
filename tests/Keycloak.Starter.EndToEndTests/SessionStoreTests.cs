using System.Security.Claims;
using Keycloak.Starter.Web.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;

namespace Keycloak.Starter.EndToEndTests;

public sealed class SessionStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "starter-tickets-" + Guid.NewGuid().ToString("N"));

    public SessionStoreTests() => Directory.CreateDirectory(_directory);

    private FileTicketStore CreateStore() => new(_directory,
        DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(_directory, "keys")),
            builder => builder.SetApplicationName("SessionStoreTests")));

    private static AuthenticationTicket Ticket(DateTimeOffset expires)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "test-user")], "Cookies"));
        var properties = new AuthenticationProperties { ExpiresUtc = expires };
        properties.StoreTokens([new AuthenticationToken { Name = "access_token", Value = "test-access-token" }]);
        return new AuthenticationTicket(principal, properties, "Cookies");
    }

    [Fact]
    public async Task A_new_store_can_decrypt_a_session_with_the_same_key_ring()
    {
        string key;
        using (var first = CreateStore())
        {
            key = await first.StoreAsync(Ticket(DateTimeOffset.UtcNow.AddHours(1)));
        }
        using var recreated = CreateStore();

        var ticket = await recreated.RetrieveAsync(key);

        Assert.NotNull(ticket);
        Assert.Equal("test-user", ticket.Principal.FindFirst("sub")?.Value);
        Assert.Equal("test-access-token", ticket.Properties.GetTokenValue("access_token"));
    }

    [Fact]
    public async Task Renewing_a_removed_session_does_not_resurrect_it()
    {
        using var store = CreateStore();
        var ticket = Ticket(DateTimeOffset.UtcNow.AddHours(1));
        var key = await store.StoreAsync(ticket);
        await store.RemoveAsync(key);

        await store.RenewAsync(key, ticket);

        Assert.Null(await store.RetrieveAsync(key));
    }

    [Fact]
    public async Task An_expired_ticket_is_not_returned()
    {
        using var store = CreateStore();
        var key = await store.StoreAsync(Ticket(DateTimeOffset.UtcNow.AddMinutes(-1)));

        Assert.Null(await store.RetrieveAsync(key));
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
