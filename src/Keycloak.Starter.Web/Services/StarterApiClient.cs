using System.Net;
using System.Net.Http.Headers;

namespace Keycloak.Starter.Web.Services;

public sealed record ApiResult(int StatusCode, string Body, bool RequiresSignIn = false);

public sealed class StarterApiClient(HttpClient client, UserAccessTokenService tokens)
{
    public async Task<ApiResult> GetAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var token = await tokens.GetAsync(cancellationToken);
            if (token is null)
            {
                return new ApiResult(401, "Sign in again to continue.", RequiresSignIn: true);
            }
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await client.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                return new ApiResult(403, "Your account does not have permission to access this page.");
            }
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return new ApiResult(401, "The API rejected the access token.");
            }
            return new ApiResult((int)response.StatusCode,
                await response.Content.ReadAsStringAsync(cancellationToken));
        }
        catch (HttpRequestException)
        {
            return new ApiResult(503, "The authentication service or API is temporarily unavailable.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ApiResult(503, "The request timed out. Please try again.");
        }
    }
}
