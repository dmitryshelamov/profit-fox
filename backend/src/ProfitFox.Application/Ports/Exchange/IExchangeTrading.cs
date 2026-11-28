using ProfitFox.Application.Exceptions;
using ProfitFox.Domain.ValueObjects.Trades;

namespace ProfitFox.Application.Ports.Exchange;

/// <summary>
///     The calls of the exchange with the key of an account that trade (change take-idea-trades, design.md, decisions 3, 4
///     and 7). The reads give the same messages the stream gives — positions, fills, orders —, so one handler takes both. A
///     read throws on a refusal, as <see cref="IExchangeKeyInspector" /> does; a write answers how the exchange took it and
///     throws only for a key the exchange does not take.
/// </summary>
public interface IExchangeTrading
{
    /// <summary>
    ///     The positions of the pair, empty ones and those of the hedge mode included; of every USDT pair with a position when
    ///     <paramref name="symbol" /> is null.
    /// </summary>
    /// <exception cref="ExchangeKeyRejectedException">The exchange does not take the key.</exception>
    /// <exception cref="ExchangeUnavailableException">The exchange did not answer.</exception>
    /// <exception cref="ExchangeException">The exchange refused for another reason.</exception>
    Task<IReadOnlyList<ExchangePosition>> PositionsAsync(ExchangeCredentials credentials, string? symbol, CancellationToken cancellationToken);

    /// <summary>
    ///     The fills of every pair from <paramref name="since" />, every page read.
    /// </summary>
    /// <exception cref="ExchangeKeyRejectedException">The exchange does not take the key.</exception>
    /// <exception cref="ExchangeUnavailableException">The exchange did not answer.</exception>
    /// <exception cref="ExchangeException">The exchange refused for another reason.</exception>
    Task<IReadOnlyList<ExchangeFill>> FillsAsync(ExchangeCredentials credentials, DateTimeOffset since, CancellationToken cancellationToken);

    /// <summary>
    ///     The order of the link id: among the open and recent ones, then in the history; null when the exchange has none.
    /// </summary>
    /// <exception cref="ExchangeKeyRejectedException">The exchange does not take the key.</exception>
    /// <exception cref="ExchangeUnavailableException">The exchange did not answer.</exception>
    /// <exception cref="ExchangeException">The exchange refused for another reason.</exception>
    Task<ExchangeOrderUpdate?> OrderAsync(ExchangeCredentials credentials, string symbol, string linkId, CancellationToken cancellationToken);

    /// <summary>
    ///     What the exchange counted for the positions of the pair closed from <paramref name="since" />; null when none closed.
    /// </summary>
    /// <exception cref="ExchangeKeyRejectedException">The exchange does not take the key.</exception>
    /// <exception cref="ExchangeUnavailableException">The exchange did not answer.</exception>
    /// <exception cref="ExchangeException">The exchange refused for another reason.</exception>
    Task<ExchangeClosedResult?> ClosedAsync(ExchangeCredentials credentials, string symbol, DateTimeOffset since, CancellationToken cancellationToken);

    /// <summary>
    ///     Sets the leverage of both sides of the pair.
    /// </summary>
    Task<ExchangeAnswer> SetLeverageAsync(ExchangeCredentials credentials, string symbol, int leverage, CancellationToken cancellationToken);

    /// <summary>
    ///     Places a market order of the one-way mode.
    /// </summary>
    Task<ExchangeAnswer> PlaceOrderAsync(ExchangeCredentials credentials, ExchangeOrderRequest order, CancellationToken cancellationToken);

    /// <summary>
    ///     Puts the stop of the whole position of the pair, by the last price.
    /// </summary>
    Task<ExchangeAnswer> SetStopAsync(ExchangeCredentials credentials, string symbol, decimal stop, CancellationToken cancellationToken);
}
