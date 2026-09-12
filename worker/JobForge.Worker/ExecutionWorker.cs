using JobForge.Application.Interfaces;
using Microsoft.Extensions.Options;

namespace JobForge.Worker;

/// <summary>
/// The resilient poll loop (spec section 37). Each iteration runs the maintenance
/// sweeps (retry promotion, stale recovery, scheduled fan-out — all cheap indexed
/// queries) and then tries to claim and fully process one execution. A failure
/// while processing one job is recorded on that execution and never escapes this
/// loop; a failure talking to the database itself is logged and backed off so the
/// process stays alive through a transient Postgres outage instead of crashing.
/// </summary>
public sealed class ExecutionWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly WorkerSettings _settings;
    private readonly ILogger<ExecutionWorker> _logger;
    private readonly string _workerId;

    public ExecutionWorker(IServiceScopeFactory scopeFactory, IOptions<WorkerSettings> settings, ILogger<ExecutionWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _logger = logger;
        _workerId = string.IsNullOrWhiteSpace(_settings.WorkerId)
            ? $"{Environment.MachineName}-{Guid.NewGuid().ToString("N")[..8]}"
            : _settings.WorkerId;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("WorkerStarted workerId={WorkerId} pollIntervalSeconds={PollIntervalSeconds} staleThresholdSeconds={StaleThresholdSeconds}",
            _workerId, _settings.PollIntervalSeconds, _settings.StaleExecutionThresholdSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            bool processedSomething;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var workerExecutionService = scope.ServiceProvider.GetRequiredService<IWorkerExecutionService>();

                await workerExecutionService.RecoverStaleExecutionsAsync(
                    TimeSpan.FromSeconds(_settings.StaleExecutionThresholdSeconds), stoppingToken);
                await workerExecutionService.PromoteDueRetriesAsync(stoppingToken);
                await workerExecutionService.CreateDueScheduledExecutionsAsync(stoppingToken);

                processedSomething = await workerExecutionService.ClaimAndProcessNextAsync(_workerId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Infrastructure failure (e.g. Postgres temporarily unreachable). Log and back off —
                // this loop must stay alive; it does not rethrow.
                _logger.LogError(ex, "WorkerIterationFailed workerId={WorkerId}; backing off {BackoffSeconds}s", _workerId, _settings.ErrorBackoffSeconds);
                await DelaySafely(TimeSpan.FromSeconds(_settings.ErrorBackoffSeconds), stoppingToken);
                continue;
            }

            if (!processedSomething)
            {
                await DelaySafely(TimeSpan.FromSeconds(_settings.PollIntervalSeconds), stoppingToken);
            }
        }

        _logger.LogInformation("WorkerStopping workerId={WorkerId}", _workerId);
    }

    private static async Task DelaySafely(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Expected during graceful shutdown.
        }
    }
}
