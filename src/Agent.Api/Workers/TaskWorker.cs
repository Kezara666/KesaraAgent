using Agent.Core.Tasks;

namespace Agent.Api.Workers;

/// <summary>
/// Pulls task IDs and runs them. On start it recovers queued and interrupted work from the database,
/// so a restart never loses a task (see <see cref="TaskRecovery"/>).
/// </summary>
public sealed partial class TaskWorker(
    ITaskDispatcher queue,
    ITaskStore store,
    TaskProcessor processor,
    TimeProvider clock,
    ILogger<TaskWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var recovered = await TaskRecovery.RecoverAsync(store, queue, clock, stoppingToken).ConfigureAwait(false);
            if (recovered > 0)
            {
                LogRecovered(logger, recovered);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            Guid taskId;
            try
            {
                taskId = await queue.DequeueAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await processor.ProcessAsync(taskId, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                LogProcessingFailure(logger, exception, taskId);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Recovered {Count} queued task(s) after restart.")]
    private static partial void LogRecovered(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Task {TaskId} could not be processed.")]
    private static partial void LogProcessingFailure(ILogger logger, Exception exception, Guid taskId);
}
