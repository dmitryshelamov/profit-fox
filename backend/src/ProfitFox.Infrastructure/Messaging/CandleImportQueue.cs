using System.Threading.Channels;

using ProfitFox.Application.Ports.MarketData;

namespace ProfitFox.Infrastructure.Messaging;

internal sealed class CandleImportQueue : ICandleImportQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>();

    public ValueTask EnqueueAsync(Guid jobId, CancellationToken cancellationToken)
    {
        return _channel.Writer.WriteAsync(jobId, cancellationToken);
    }

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken cancellationToken)
    {
        return _channel.Reader.ReadAllAsync(cancellationToken);
    }
}