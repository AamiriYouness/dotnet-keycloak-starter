using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Keycloak.Starter.EndToEndTests;

[Trait("Category", "Browser")]
public sealed class AuthenticationJourneyTests(WebFixture fixture) : IClassFixture<WebFixture>
{
    private async Task LoginAsync(IPage page, string username, string password, string target = "/Profile")
    {
        await page.GotoAsync(fixture.WebUrl + target);
        await page.Locator("#username").FillAsync(username);
        await page.Locator("#password").FillAsync(password);
        await page.Locator("#kc-login").ClickAsync();
        await page.WaitForURLAsync(fixture.WebUrl + target);
        await Expect(page.GetByTestId("api-status")).ToHaveTextAsync("HTTP 200");
    }

    [Fact]
    public async Task Anonymous_protected_request_redirects_to_keycloak_with_pkce()
    {
        await using var context = await fixture.NewContextAsync();
        var page = await context.NewPageAsync();
        var authorizationRequest = page.WaitForRequestAsync(request => request.Url.StartsWith(
            fixture.KeycloakUrl + "/realms/starter/protocol/openid-connect/auth", StringComparison.Ordinal));
        await page.GotoAsync(fixture.WebUrl + "/Profile");
        if (!await page.Locator("#username").IsVisibleAsync())
        {
            var text = await page.Locator("body").InnerTextAsync();
            throw new InvalidOperationException("Unexpected login page at " +
                new Uri(page.Url).GetLeftPart(UriPartial.Path) + ": " + text[..Math.Min(text.Length, 1200)]);
        }
        await Expect(page.Locator("#username")).ToBeVisibleAsync();
        Assert.StartsWith(fixture.KeycloakUrl + "/realms/starter/", page.Url);
        // With PAR, the provider carries PKCE in the pushed request instead of the browser URL.
        var authorizationUrl = (await authorizationRequest).Url;
        Assert.True(authorizationUrl.Contains("request_uri=", StringComparison.Ordinal) ||
            (authorizationUrl.Contains("code_challenge=", StringComparison.Ordinal) &&
             authorizationUrl.Contains("code_challenge_method=S256", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("demo-reader", "local-reader-password", "/Reader", 200, "/Admin", 403)]
    [InlineData("demo-admin", "local-admin-user-password", "/Admin", 200, "/Reader", 403)]
    [InlineData("demo-unprivileged", "local-user-password", "/Reader", 403, "/Admin", 403)]
    public async Task User_permissions_are_enforced_by_the_api(
        string username, string password, string firstPath, int firstStatus, string secondPath, int secondStatus)
    {
        await using var context = await fixture.NewContextAsync();
        var page = await context.NewPageAsync();
        await LoginAsync(page, username, password);
        var first = await page.GotoAsync(fixture.WebUrl + firstPath);
        Assert.Equal(firstStatus, first!.Status);
        await Expect(page.GetByTestId("api-status")).ToHaveTextAsync("HTTP " + firstStatus);
        var second = await page.GotoAsync(fixture.WebUrl + secondPath);
        Assert.Equal(secondStatus, second!.Status);
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Access denied" })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Authentication_survives_container_recreation()
    {
        await using var context = await fixture.NewContextAsync();
        var page = await context.NewPageAsync();
        await LoginAsync(page, "demo-reader", "local-reader-password");
        var session = Assert.Single(await context.CookiesAsync(), cookie => cookie.Name == "__Host-StarterWeb");
        Assert.True(session.HttpOnly);
        Assert.True(session.Secure);

        await fixture.RecreateWebAsync();
        await page.GotoAsync(fixture.WebUrl + "/Profile");

        await Expect(page.GetByTestId("api-status")).ToHaveTextAsync("HTTP 200");
        Assert.StartsWith(fixture.WebUrl, page.Url);
    }

    [Fact]
    public async Task Expired_access_token_is_refreshed_without_requiring_login()
    {
        await using var context = await fixture.NewContextAsync();
        var page = await context.NewPageAsync();
        await LoginAsync(page, "demo-reader", "local-reader-password");
        await Task.Delay(TimeSpan.FromSeconds(7), TestContext.Current.CancellationToken);

        await page.GotoAsync(fixture.WebUrl + "/Reader");

        await Expect(page.GetByTestId("api-status")).ToHaveTextAsync("HTTP 200");
        Assert.StartsWith(fixture.WebUrl, page.Url);
    }

    [Fact]
    public async Task Logout_ends_sso_and_invalidates_the_old_session_cookie()
    {
        await using var context = await fixture.NewContextAsync();
        var page = await context.NewPageAsync();
        await LoginAsync(page, "demo-reader", "local-reader-password");
        var cookie = Assert.Single(await context.CookiesAsync(), value => value.Name == "__Host-StarterWeb");
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign out", Exact = true }).ClickAsync();
        await page.WaitForURLAsync(fixture.WebUrl + "/SignedOut");
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "You are signed out." })).ToBeVisibleAsync();
        await page.GotoAsync(fixture.WebUrl + "/Profile");
        await Expect(page.Locator("#username")).ToBeVisibleAsync();

        await using var replay = await fixture.NewContextAsync();
        await replay.AddCookiesAsync([new Cookie
        {
            Name = cookie.Name, Value = cookie.Value, Domain = cookie.Domain,
            Path = cookie.Path, Secure = cookie.Secure, HttpOnly = cookie.HttpOnly
        }]);
        var replayPage = await replay.NewPageAsync();
        await replayPage.GotoAsync(fixture.WebUrl + "/Profile");
        await Expect(replayPage.Locator("#username")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Logout_requires_an_antiforgery_token()
    {
        await using var context = await fixture.NewContextAsync();
        var page = await context.NewPageAsync();
        await LoginAsync(page, "demo-reader", "local-reader-password");

        var response = await context.APIRequest.PostAsync(fixture.WebUrl + "/Account/Logout");

        Assert.Equal(400, response.Status);
        await page.GotoAsync(fixture.WebUrl + "/Profile");
        await Expect(page.GetByTestId("api-status")).ToHaveTextAsync("HTTP 200");
    }

    [Fact]
    public async Task Login_return_url_cannot_redirect_to_an_external_site()
    {
        await using var context = await fixture.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync(fixture.WebUrl + "/Account/Login?returnUrl=https%3A%2F%2Fexample.invalid");
        await page.Locator("#username").FillAsync("demo-reader");
        await page.Locator("#password").FillAsync("local-reader-password");
        await page.Locator("#kc-login").ClickAsync();

        await page.WaitForURLAsync(fixture.WebUrl + "/");
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Open your profile" })).ToBeVisibleAsync();
    }
}
