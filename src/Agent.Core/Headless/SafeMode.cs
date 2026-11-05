using Agent.Core.Notifications;

namespace Agent.Core.Headless;

/// <summary>
/// Enters safe mode after repeated model failures. In safe mode no new tool is promoted, while existing tools
/// and schedules keep running (FR-7.3). Entering safe mode notifies the operator out-of-band (FR-7.4).
/// </summary>
public sealed class SafeModeController(INotifier notifier)
{
    public const int FailuresBeforeSafeMode = 3;

    private int consecutiveFailures;

    public bool IsSafeMode { get; private set; }

    public bool AllowsPromotion => !IsSafeMode;

    public async Task RecordModelFailureAsync(CancellationToken cancellationToken)
    {
        consecutiveFailures++;
        if (IsSafeMode || consecutiveFailures < FailuresBeforeSafeMode)
        {
            return;
        }

        IsSafeMode = true;
        await notifier.NotifyAsync(
            "Agent entered safe mode",
            "The model failed repeatedly. New tool promotions are paused; existing tools and schedules still run.",
            cancellationToken).ConfigureAwait(false);
    }

    public void RecordModelSuccess()
    {
        consecutiveFailures = 0;
        IsSafeMode = false;
    }
}
