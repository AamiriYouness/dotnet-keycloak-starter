using Microsoft.Playwright;

namespace Keycloak.Starter.EndToEndTests;

public sealed class BrowserNavigationTests
{
    [Fact]
    public async Task Network_change_is_retried_once()
    {
        var attempts = 0;
        var result = await BrowserNavigation.GoToAsync(() =>
        {
            if (++attempts == 1) throw new PlaywrightException("net::ERR_NETWORK_CHANGED at https://app.localhost/Profile");
            return Task.FromResult<IResponse?>(null);
        }, TestContext.Current.CancellationToken);
        Assert.Null(result);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task Repeated_network_change_fails_after_two_attempts()
    {
        var attempts = 0;
        var failure = new PlaywrightException("net::ERR_NETWORK_CHANGED at https://app.localhost/Profile");
        var actual = await Assert.ThrowsAsync<PlaywrightException>(() => BrowserNavigation.GoToAsync(() =>
        { attempts++; throw failure; }, TestContext.Current.CancellationToken));
        Assert.Same(failure, actual);
        Assert.Equal(2, attempts);
    }

    [Theory]
    [InlineData("net::ERR_CONNECTION_REFUSED")]
    [InlineData("net::ERR_CERT_AUTHORITY_INVALID")]
    [InlineData("net::ERR_NAME_NOT_RESOLVED")]
    public async Task Other_browser_errors_are_not_retried(string message)
    {
        var attempts = 0;
        var failure = new PlaywrightException(message);
        var actual = await Assert.ThrowsAsync<PlaywrightException>(() => BrowserNavigation.GoToAsync(() =>
        { attempts++; throw failure; }, TestContext.Current.CancellationToken));
        Assert.Same(failure, actual);
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task Non_browser_exceptions_are_not_retried()
    {
        var attempts = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() => BrowserNavigation.GoToAsync(() =>
        { attempts++; throw new InvalidOperationException("Unexpected failure"); }, TestContext.Current.CancellationToken));
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task Cancellation_prevents_retry()
    {
        using var cancellation = new CancellationTokenSource();
        var attempts = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => BrowserNavigation.GoToAsync(() =>
        {
            attempts++;
            cancellation.Cancel();
            throw new PlaywrightException("net::ERR_NETWORK_CHANGED");
        }, cancellation.Token));
        Assert.Equal(1, attempts);
    }
}
