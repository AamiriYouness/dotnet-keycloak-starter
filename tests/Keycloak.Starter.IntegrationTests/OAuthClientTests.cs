extern alias WorkerApp;

using WorkerApp::Keycloak.Starter.Worker.Configuration;
using Keycloak.Starter.Api.Configuration;
using System.Net;
using System.Text;
using Keycloak.Starter.Api.Delegation;
using WorkerApp::Keycloak.Starter.Worker;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Keycloak.Starter.IntegrationTests;

internal sealed class TestEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = "Development";
    public string ApplicationName { get; set; } = "Tests";
    public string ContentRootPath { get; set; } = "/tmp";
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}

internal sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
}

internal sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { BaseAddress = new Uri("https://api.test/") };
}

internal sealed class ManualClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => Now;
}

public sealed class OAuthClientTests
{
    private const string Metadata = "{\"issuer\":\"https://issuer.test/realms/starter\",\"token_endpoint\":\"https://issuer.test/token\"}";
    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private static ServiceTokenProvider Provider(HttpMessageHandler handler, TimeProvider? clock = null) =>
        new(new StubFactory(handler), Options.Create(new WorkerSettings
        {
            Authority = "https://issuer.test/realms/starter", ClientId = "starter-worker", ClientSecret = "never-print-me"
        }), new TestEnvironment(), clock ?? TimeProvider.System);

    [Fact]
    public async Task Concurrent_requests_share_one_token_and_renew_before_expiration()
    {
        var requests = 0;
        var clock = new ManualClock();
        using var handler = new StubHandler(async (request, ct) =>
        {
            if (request.Method == HttpMethod.Get) return Json(Metadata);
            var fields = await request.Content!.ReadAsStringAsync(ct);
            Assert.Contains("grant_type=client_credentials", fields);
            Assert.DoesNotContain("subject_token", fields);
            var count = Interlocked.Increment(ref requests);
            await Task.Delay(20, ct);
            return Json($"{{\"access_token\":\"token-{count}\",\"expires_in\":300,\"token_type\":\"Bearer\"}}");
        });
        using var tokens = Provider(handler, clock);
        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => tokens.GetAsync(TestContext.Current.CancellationToken)));
        Assert.All(results, token => Assert.Equal("token-1", token));
        Assert.Equal(1, requests);
        clock.Now = clock.Now.AddSeconds(269);
        Assert.Equal("token-1", await tokens.GetAsync(TestContext.Current.CancellationToken));
        clock.Now = clock.Now.AddSeconds(2);
        Assert.Equal("token-2", await tokens.GetAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, requests);
    }

    [Theory]
    [InlineData("{\"error\":\"invalid_client\",\"error_description\":\"never-print-me\"}", 401)]
    [InlineData("not-json never-print-me", 200)]
    [InlineData("{\"access_token\":\"never-print-me\"}", 200)]
    [InlineData("null", 200)]
    [InlineData("{\"access_token\":null,\"token_type\":\"Bearer\",\"expires_in\":300}", 200)]
    [InlineData("{\"access_token\":\"never-print-me\",\"token_type\":\"Bearer\",\"expires_in\":\"300\"}", 200)]
    public async Task Acquisition_errors_do_not_expose_provider_content(string body, int status)
    {
        using var handler = new StubHandler((request, _) => Task.FromResult(request.Method == HttpMethod.Get
            ? Json(Metadata) : Json(body, (HttpStatusCode)status)));
        using var tokens = Provider(handler);
        var exception = await Assert.ThrowsAsync<TokenAcquisitionException>(() => tokens.GetAsync(TestContext.Current.CancellationToken));
        Assert.DoesNotContain("never-print-me", exception.ToString());
        Assert.Null(exception.InnerException);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_reaches_discovery_and_token_requests(bool cancelTokenRequest)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new StubHandler(async (request, ct) =>
        {
            if (cancelTokenRequest && request.Method == HttpMethod.Get) return Json(Metadata);
            entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return Json("{}");
        });
        using var tokens = Provider(handler);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var pending = tokens.GetAsync(cancel.Token);
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
    }

    [Fact]
    public async Task Canceled_waiter_does_not_interrupt_another_token_acquisition()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new StubHandler(async (request, ct) =>
        {
            if (request.Method == HttpMethod.Get) return Json(Metadata);
            entered.TrySetResult();
            await release.Task.WaitAsync(ct);
            return Json("{\"access_token\":\"one\",\"expires_in\":300,\"token_type\":\"Bearer\"}");
        });
        using var provider = Provider(handler);
        var first = provider.GetAsync(TestContext.Current.CancellationToken);
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        using var cancel = new CancellationTokenSource();
        var waiter = provider.GetAsync(cancel.Token);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await waiter);
        release.SetResult();
        Assert.Equal("one", await first);
    }

    [Fact]
    public async Task Bearer_handler_propagates_cancellation_to_api()
    {
        using var oauth = new StubHandler((request, _) => Task.FromResult(Json(request.Method == HttpMethod.Get
            ? Metadata : "{\"access_token\":\"service-token\",\"expires_in\":300,\"token_type\":\"Bearer\"}")));
        using var tokens = Provider(oauth);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var bearer = new ServiceBearerHandler(tokens)
        {
            InnerHandler = new StubHandler(async (request, ct) =>
            {
                Assert.Equal("service-token", request.Headers.Authorization?.Parameter);
                entered.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                return Json("{}");
            })
        };
        using var client = new HttpClient(bearer);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var pending = client.GetAsync("https://api.test/sync/status", cancel.Token);
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
    }

    private static ServiceProvider ExchangeServices(HttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHttpClientFactory>(new StubFactory(handler));
        services.AddSingleton<IHostEnvironment>(new TestEnvironment());
        services.Configure<ExchangeSettings>(s => { s.Enabled = true; s.ClientSecret = "never-print-me"; });
        services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            options.Authority = "https://issuer.test/realms/starter";
            options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(new()
            { Issuer = options.Authority, TokenEndpoint = "https://issuer.test/token" });
        });
        services.AddTransient<TokenExchangeClient>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Exchange_sends_encoded_parameters_and_does_not_retry_or_expose_errors()
    {
        var count = 0;
        using var handler = new StubHandler(async (request, ct) =>
        {
            Interlocked.Increment(ref count);
            Assert.Equal("application/x-www-form-urlencoded", request.Content!.Headers.ContentType!.MediaType);
            var body = await request.Content.ReadAsStringAsync(ct);
            Assert.Contains("subject_token=user%2Btoken%26value", body);
            Assert.Contains("audience=starter-transactions", body);
            return Json("{\"error\":\"invalid_grant\",\"error_description\":\"never-print-me\"}", HttpStatusCode.BadRequest);
        });
        await using var services = ExchangeServices(handler);
        var exception = await Assert.ThrowsAsync<TokenExchangeException>(() => services.GetRequiredService<TokenExchangeClient>()
            .ExchangeAsync("user+token&value", TestContext.Current.CancellationToken));
        Assert.Equal(1, count);
        Assert.DoesNotContain("never-print-me", exception.ToString());
        Assert.DoesNotContain("user+token", exception.ToString());
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("not-json never-print-me")]
    [InlineData("{\"access_token\":42,\"token_type\":\"Bearer\",\"issued_token_type\":\"urn:ietf:params:oauth:token-type:access_token\"}")]
    [InlineData("{\"access_token\":null,\"token_type\":\"Bearer\",\"issued_token_type\":\"urn:ietf:params:oauth:token-type:access_token\"}")]
    [InlineData("{\"access_token\":\"never-print-me\",\"token_type\":\"Bearer\",\"issued_token_type\":\"urn:ietf:params:oauth:token-type:id_token\"}")]
    public async Task Invalid_exchange_response_is_rejected_without_exposing_content(string body)
    {
        using var handler = new StubHandler((_, _) => Task.FromResult(Json(body)));
        await using var services = ExchangeServices(handler);
        var exception = await Assert.ThrowsAsync<TokenExchangeException>(() => services.GetRequiredService<TokenExchangeClient>()
            .ExchangeAsync("subject", TestContext.Current.CancellationToken));
        Assert.DoesNotContain("never-print-me", exception.ToString());
        Assert.Null(exception.InnerException);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{\"issuer\":\"https://issuer.test/realms/starter\"}")]
    [InlineData("{\"issuer\":\"https://other.test\",\"token_endpoint\":\"https://other.test/token\"}")]
    public async Task Invalid_discovery_prevents_sending_credentials(string body)
    {
        using var handler = new StubHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            return Task.FromResult(Json(body));
        });
        using var tokens = Provider(handler);
        var exception = await Assert.ThrowsAsync<TokenAcquisitionException>(() => tokens.GetAsync(TestContext.Current.CancellationToken));
        Assert.DoesNotContain("never-print-me", exception.ToString());
    }

    [Fact]
    public async Task Exchange_propagates_cancellation()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new StubHandler(async (_, ct) =>
        {
            entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return Json("{}");
        });
        await using var services = ExchangeServices(handler);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var pending = services.GetRequiredService<TokenExchangeClient>().ExchangeAsync("subject", cancel.Token);
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
    }
}

public sealed class WorkerJobTests
{
    [Theory]
    [InlineData(401, 1)]
    [InlineData(403, 1)]
    [InlineData(503, 3)]
    public async Task Job_retries_only_transient_api_failures_and_bounds_attempts(int status, int expectedAttempts)
    {
        var attempts = 0;
        using var handler = new StubHandler((_, _) =>
        {
            Interlocked.Increment(ref attempts);
            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status));
        });
        using var worker = new SynchronizationWorker(new StubFactory(handler), Options.Create(new WorkerSettings()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SynchronizationWorker>.Instance);
        await worker.RunOnceAsync(TestContext.Current.CancellationToken);
        Assert.Equal(expectedAttempts, attempts);
    }

    [Fact]
    public async Task Job_does_not_retry_oauth_errors()
    {
        var attempts = 0;
        using var handler = new StubHandler((_, _) =>
        {
            Interlocked.Increment(ref attempts);
            throw new TokenAcquisitionException();
        });
        using var worker = new SynchronizationWorker(new StubFactory(handler), Options.Create(new WorkerSettings()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SynchronizationWorker>.Instance);
        await worker.RunOnceAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task Worker_shutdown_cancels_active_http_request()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new StubHandler(async (_, ct) =>
        {
            entered.SetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
            catch (OperationCanceledException) { canceled.SetResult(); throw; }
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var worker = new SynchronizationWorker(new StubFactory(handler), Options.Create(new WorkerSettings()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SynchronizationWorker>.Instance);
        await worker.StartAsync(TestContext.Current.CancellationToken);
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        await worker.StopAsync(TestContext.Current.CancellationToken);
        await canceled.Task.WaitAsync(TestContext.Current.CancellationToken);
    }
}
