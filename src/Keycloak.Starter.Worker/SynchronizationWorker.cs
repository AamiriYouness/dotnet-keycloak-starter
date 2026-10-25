using Keycloak.Starter.Worker.Configuration;
using System.Net;
using Microsoft.Extensions.Options;

namespace Keycloak.Starter.Worker;

public sealed class SynchronizationWorker(
    IHttpClientFactory clients, IOptions<WorkerSettings> settings, ILogger<SynchronizationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(settings.Value.IntervalSeconds));
        try
        {
            do { await RunOnceAsync(stoppingToken); }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        // A maximum of three attempts, only for idempotent API GETs and transient failures.
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var client = clients.CreateClient("sync");
                using var response = await client.GetAsync("sync/status", cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    logger.LogInformation("Synchronization status checked successfully (HTTP {Status})", (int)response.StatusCode);
                    return;
                }
                var transient = response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests ||
                    (int)response.StatusCode >= 500;
                logger.LogWarning("Synchronization check returned HTTP {Status}; attempt {Attempt}", (int)response.StatusCode, attempt);
                if (!transient) return;
            }
            catch (TokenAcquisitionException)
            {
                logger.LogWarning("Service token acquisition failed; waiting for the next scheduled check");
                return; // Never retry an OAuth error in this job.
            }
            catch (HttpRequestException) { logger.LogWarning("Synchronization dependency unavailable; attempt {Attempt}", attempt); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            { logger.LogWarning("Synchronization request timed out; attempt {Attempt}", attempt); }
            if (attempt < 3) await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken);
        }
    }
}
