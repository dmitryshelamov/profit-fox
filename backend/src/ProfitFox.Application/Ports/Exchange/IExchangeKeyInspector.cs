using ProfitFox.Application.Exceptions;
using ProfitFox.Domain.ValueObjects.Accounts;

namespace ProfitFox.Application.Ports.Exchange;

/// <summary>
///     Reads Bybit with the key of an account (change add-exchange-accounts, design.md, decisions 4, 7 and 11): what the key
///     may do, the money of the unified account free for a new position, and its margin mode. Neither call changes anything at the exchange.
/// </summary>
public interface IExchangeKeyInspector
{
    /// <summary>
    ///     The permissions of the key, the UID and the kind of its account.
    /// </summary>
    /// <exception cref="ExchangeKeyRejectedException">Bybit does not take the key or the secret.</exception>
    /// <exception cref="ExchangeUnavailableException">Bybit did not answer, answered 403 or 5xx, or the limit is hit.</exception>
    /// <exception cref="ExchangeException">Bybit refused the call for another reason.</exception>
    Task<ExchangeKeyInfo> InspectAsync(string apiKey, string secret, CancellationToken cancellationToken);

    /// <summary>
    ///     The available balance of the unified account, as Bybit counts it in dollars.
    /// </summary>
    /// <exception cref="ExchangeKeyRejectedException">Bybit does not take the key or the secret.</exception>
    /// <exception cref="ExchangeUnavailableException">Bybit did not answer, answered 403 or 5xx, or the limit is hit.</exception>
    /// <exception cref="ExchangeException">Bybit refused the call for another reason.</exception>
    Task<decimal> FreeAsync(string apiKey, string secret, CancellationToken cancellationToken);

    /// <summary>
    ///     The margin mode of the unified account as Bybit names it — <c>ISOLATED_MARGIN</c>, <c>REGULAR_MARGIN</c>,
    ///     <c>PORTFOLIO_MARGIN</c> or a mode the bench does not know — and null when Bybit sent none (design.md, decision 11).
    /// </summary>
    /// <exception cref="ExchangeKeyRejectedException">Bybit does not take the key or the secret.</exception>
    /// <exception cref="ExchangeUnavailableException">Bybit did not answer, answered 403 or 5xx, or the limit is hit.</exception>
    /// <exception cref="ExchangeException">Bybit refused the call for another reason.</exception>
    Task<string?> MarginModeAsync(string apiKey, string secret, CancellationToken cancellationToken);
}
