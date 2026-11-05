using Agent.Core.Tasks;

namespace Agent.Api.Workers;

/// <summary>
/// Pulls queued task IDs and runs them. Runs inside the API process for feat/core-api;
/// moves to Agent.Worker when infra/queue is added.
/// </summary>
public sealed partial class TaskWorker(ITaskDispatcher queue, TaskProcessor processor, ILogger<TaskWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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

    [LoggerMessage(Level = LogLevel.Error, Message = "Task {TaskId} could not be processed.")]
    private static partial void LogProcessingFailure(ILogger logger, Exception exception, Guid taskId);
}
