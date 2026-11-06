using Agent.Core.Headless;
using Agent.Core.Notifications;
using Xunit;

namespace Agent.Core.Tests.Headless;

public sealed class HeadlessTests
{
    private static readonly DateTimeOffset Morning = new(2026, 11, 9, 7, 0, 30, TimeSpan.Zero);

    [Fact]
    public void FR98PreviewShowsTheNextOccurrencesBeforeSaving()
    {
        var next = CronPreview.NextOccurrences("0 7 * * 1-5", new DateTimeOffset(2026, 11, 6, 0, 0, 0, TimeSpan.Zero), 3);

        Assert.Equal(3, next.Count);
        Assert.All(next, occurrence => Assert.Equal(7, occurrence.UtcDateTime.Hour));
        Assert.All(next, occurrence => Assert.True(occurrence.UtcDateTime.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday));
    }

    [Fact]
    public void FR98InvalidCronIsReportedNotSaved()
    {
        var (expression, error) = CronPreview.Parse("every morning please");

        Assert.Null(expression);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task FR71DueScheduleRunsWithNoClientAndOnlyOncePerOccurrence()
    {
        var store = new InMemorySchedules(new Schedule(Guid.CreateVersion7(), "morning", "0 7 * * *", "summarize mail", true, null));
        var jobs = new RecordingJobs();
        var scheduler = new SchedulerService(store, jobs, new FixedClock(Morning));

        var first = await scheduler.TickAsync(CancellationToken.None);
        var second = await scheduler.TickAsync(CancellationToken.None);

        Assert.Equal(["morning"], first);
        Assert.Empty(second);
        Assert.Equal(1, jobs.Runs);
    }

    [Fact]
    public async Task FR71DisabledScheduleDoesNotRun()
    {
        var store = new InMemorySchedules(new Schedule(Guid.CreateVersion7(), "off", "0 7 * * *", "x", false, null));
        var jobs = new RecordingJobs();

        await new SchedulerService(store, jobs, new FixedClock(Morning)).TickAsync(CancellationToken.None);

        Assert.Equal(0, jobs.Runs);
    }

    [Fact]
    public async Task FR73ModelFailuresEnterSafeModeWhichBlocksPromotionAndNotifiesOnce()
    {
        var notifier = new RecordingNotifier();
        var safe = new SafeModeController(notifier);

        for (var index = 0; index < SafeModeController.FailuresBeforeSafeMode; index++)
        {
            await safe.RecordModelFailureAsync(CancellationToken.None);
        }
        await safe.RecordModelFailureAsync(CancellationToken.None);

        Assert.True(safe.IsSafeMode);
        Assert.False(safe.AllowsPromotion);
        Assert.Equal(1, notifier.Sent);
    }

    [Fact]
    public async Task FR73ScheduledWorkStillRunsWhileTheModelIsDown()
    {
        var safe = new SafeModeController(new RecordingNotifier());
        for (var index = 0; index < SafeModeController.FailuresBeforeSafeMode; index++)
        {
            await safe.RecordModelFailureAsync(CancellationToken.None);
        }
        var jobs = new RecordingJobs();
        var store = new InMemorySchedules(new Schedule(Guid.CreateVersion7(), "morning", "0 7 * * *", "summarize mail", true, null));

        var ran = await new SchedulerService(store, jobs, new FixedClock(Morning)).TickAsync(CancellationToken.None);

        Assert.True(safe.IsSafeMode);
        Assert.Equal(["morning"], ran);
    }

    [Fact]
    public async Task FR73ASuccessfulModelCallLeavesSafeMode()
    {
        var safe = new SafeModeController(new RecordingNotifier());
        for (var index = 0; index < SafeModeController.FailuresBeforeSafeMode; index++)
        {
            await safe.RecordModelFailureAsync(CancellationToken.None);
        }

        safe.RecordModelSuccess();

        Assert.False(safe.IsSafeMode);
        Assert.True(safe.AllowsPromotion);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class InMemorySchedules(params Schedule[] initial) : IScheduleStore
    {
        private readonly Dictionary<Guid, Schedule> items = initial.ToDictionary(schedule => schedule.Id);

        public Task AddAsync(Schedule schedule, CancellationToken cancellationToken)
        {
            items[schedule.Id] = schedule;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Schedule schedule, CancellationToken cancellationToken)
        {
            items[schedule.Id] = schedule;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Schedule>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Schedule>>(items.Values.ToList());
    }

    private sealed class RecordingJobs : IScheduledJobRunner
    {
        public int Runs { get; private set; }

        public Task RunAsync(Schedule schedule, CancellationToken cancellationToken)
        {
            Runs++;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingNotifier : INotifier
    {
        public int Sent { get; private set; }

        public Task NotifyAsync(string title, string message, CancellationToken cancellationToken)
        {
            Sent++;
            return Task.CompletedTask;
        }
    }
}
