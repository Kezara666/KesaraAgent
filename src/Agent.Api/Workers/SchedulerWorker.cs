using Agent.Core.Headless;

namespace Agent.Api.Workers;

/// <summary>Checks schedules every 30 seconds. Runs whether or not a client is connected (FR-7.2).</summary>
public sealed partial class SchedulerWorker(SchedulerService scheduler, ILogger<SchedulerWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                var ran = await scheduler.TickAsync(stoppingToken).ConfigureAwait(false);
                if (ran.Count > 0)
                {
                    LogSubmitted(logger, ran.Count);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                LogTickFailed(logger, exception);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Submitted {Count} scheduled task(s).")]
    private static partial void LogSubmitted(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Scheduler tick failed; it will retry on the next interval.")]
    private static partial void LogTickFailed(ILogger logger, Exception exception);
}
