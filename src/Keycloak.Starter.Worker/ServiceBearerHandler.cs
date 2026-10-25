using System.Net.Http.Headers;

namespace Keycloak.Starter.Worker;

public sealed class ServiceBearerHandler(ServiceTokenProvider tokens) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetAsync(cancellationToken));
        return await base.SendAsync(request, cancellationToken);
    }
}
