using Microsoft.Playwright;

namespace Keycloak.Starter.EndToEndTests;

internal static class BrowserNavigation
{
    public static Task<IResponse?> GoToAsync(IPage page, string url) =>
        GoToAsync(() => page.GotoAsync(url), TestContext.Current.CancellationToken);

    internal static async Task<IResponse?> GoToAsync(Func<Task<IResponse?>> navigate, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try { return await navigate(); }
        catch (PlaywrightException exception) when (exception.Message.StartsWith("net::ERR_NETWORK_CHANGED", StringComparison.Ordinal))
        {
            // Docker bridge/veth changes can interrupt Chromium's initial GET on Linux.
            // Retry this transport error once; a second failure propagates unchanged.
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
            return await navigate();
        }
    }
}
