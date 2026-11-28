using Bybit.Net.Clients;
using Bybit.Net.Objects.Options;

using CryptoExchange.Net.Objects;
using CryptoExchange.Net.Objects.Sockets;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.Exchange;

namespace ProfitFox.Infrastructure.Clients.Bybit;

/// <summary>
///     The public stream of the mark prices on <c>BybitSocketClient</c> (change take-idea-trades, design.md, decision 3):
///     <c>tickers.{pair}</c> of the linear category. One client for the process, so every pair of the Worker goes over the
///     connections the package shares; a subscription closes only its pair. A message without the mark price — Bybit sends
///     the changed fields only — passes nothing.
/// </summary>
internal sealed class BybitPriceStream(BybitSettings settings, ILogger<BybitPriceStream>? logger = null) : IExchangePriceStream, IDisposable
{
    private readonly ILogger _logger = (ILogger?)logger ?? NullLogger.Instance;

    private readonly Lazy<BybitSocketClient> _client = new Lazy<BybitSocketClient>(() =>
        new BybitSocketClient(Options.Create(new BybitSocketOptions { Environment = settings.Environment() }), NullLoggerFactory.Instance));

    public async Task<IExchangeSubscription> OpenAsync(string symbol, Action<string, decimal> onMark, Action<ExchangeStreamState> onState, CancellationToken cancellationToken)
    {
        WebSocketResult<UpdateSubscription> result = await _client.Value.V5LinearApi.SubscribeToTickerUpdatesAsync(
            symbol,
            update =>
            {
                if (update.Data.MarkPrice is > 0 and { } mark)
                {
                    onMark(update.Data.Symbol, mark);
                }
            },
            cancellationToken);
        if (!result.Success)
        {
            _logger.LogWarning("Bybit refused the subscription of the prices of {Symbol}: {Code}, {Reason}", symbol, result.Error?.ErrorCode, result.Error?.Message);
            throw new ExchangeException($"Bybit не открыл поток цен {symbol}: {result.Error?.Message}");
        }

        var subscription = new BybitStreamSubscription(new Nothing(), onState);
        subscription.Add(result.Data!);
        return subscription;
    }

    public void Dispose()
    {
        if (_client.IsValueCreated)
        {
            _client.Value.Dispose();
        }
    }

    // The client of the prices is the process's: closing a pair leaves it open.
    private sealed class Nothing : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
