using System.Threading.Channels;

using ProfitFox.Application.Ports.MarketData;

namespace ProfitFox.Infrastructure.Messaging;

internal sealed class CandleImportQueue : ICandleImportQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>();

    public ValueTask EnqueueAsync(Guid jobId, CancellationToken cancellationToken) => _channel.Writer.WriteAsync(jobId, cancellationToken);

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken cancellationToken) => _channel.Reader.ReadAllAsync(cancellationToken);
}
