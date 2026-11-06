using System.Threading.Channels;
using Agent.Core.Tasks;

namespace Agent.Infrastructure.Queue;

/// <summary>In-process queue for feat/core-api. Replaced by Hangfire in infra/queue.</summary>
public sealed class ChannelTaskDispatcher : ITaskDispatcher
{
    private readonly Channel<Guid> channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions
    {
        SingleReader = false,
        SingleWriter = false,
    });

    public ValueTask EnqueueAsync(Guid taskId, CancellationToken cancellationToken) =>
        channel.Writer.WriteAsync(taskId, cancellationToken);

    public ValueTask<Guid> DequeueAsync(CancellationToken cancellationToken) =>
        channel.Reader.ReadAsync(cancellationToken);
}
