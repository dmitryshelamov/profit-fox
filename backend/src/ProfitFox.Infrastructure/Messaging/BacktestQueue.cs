using System.Threading.Channels;

using ProfitFox.Application.Ports.Backtests;

namespace ProfitFox.Infrastructure.Messaging;

internal sealed class BacktestQueue : IBacktestQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>();

    public ValueTask EnqueueAsync(Guid backtestId, CancellationToken cancellationToken)
    {
        return _channel.Writer.WriteAsync(backtestId, cancellationToken);
    }

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken cancellationToken)
    {
        return _channel.Reader.ReadAllAsync(cancellationToken);
    }
}