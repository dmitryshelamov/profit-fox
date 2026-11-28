using ProfitFox.Domain.ValueObjects.Trades;

namespace ProfitFox.Application.Ports.Exchange;

/// <summary>
///     What became of a connection of the stream (change take-idea-trades, design.md, decision 3): lost — the package
///     reconnects by itself —, restored with its subscriptions, or closed for good — the package gave up, and the stream is
///     opened anew.
/// </summary>
public enum ExchangeStreamState
{
    Lost,
    Restored,
    Closed
}

/// <summary>
///     A subscription of a stream; disposing of it closes it.
/// </summary>
public interface IExchangeSubscription : IAsyncDisposable;

/// <summary>
///     The private stream of an account (change take-idea-trades, design.md, decision 3): one connection with the topics
///     <c>order</c>, <c>execution</c> and <c>position</c>, which gives the messages the reads of <see cref="IExchangeTrading" />
///     give, and tells what becomes of the connection. The handlers are called on the threads of the stream.
/// </summary>
public interface IExchangeStream
{
    /// <summary>
    ///     Opens the stream of the account and returns once the exchange confirmed the three subscriptions.
    /// </summary>
    /// <exception cref="Exceptions.ExchangeException">The stream did not open or a subscription was refused.</exception>
    Task<IExchangeSubscription> OpenAsync(ExchangeCredentials credentials, Action<ExchangeMessage> onMessage, Action<ExchangeStreamState> onState, CancellationToken cancellationToken);
}

/// <summary>
///     The public stream of the prices (change take-idea-trades, design.md, decision 3): the mark price of a pair by
///     <c>tickers.{pair}</c>, all pairs of the Worker over one connection.
/// </summary>
public interface IExchangePriceStream
{
    /// <summary>
    ///     Subscribes to the mark price of the pair and returns once the exchange confirmed it.
    /// </summary>
    /// <exception cref="Exceptions.ExchangeException">The subscription was refused.</exception>
    Task<IExchangeSubscription> OpenAsync(string symbol, Action<string, decimal> onMark, Action<ExchangeStreamState> onState, CancellationToken cancellationToken);
}
