using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Application.Ports.LiveTrading;

/// <summary>Reads what an account holds on the exchange with its key. Never sends orders.</summary>
public interface IExchangeLedger
{
    /// <summary>The money of the coin the account may still trade with: what it holds less what its open orders lock.</summary>
    /// <exception cref="ProfitFox.Application.Exceptions.ExchangeKeyRejectedException">The exchange does not accept the key.</exception>
    /// <exception cref="ProfitFox.Application.Exceptions.ExchangeUnavailableException">The exchange did not answer.</exception>
    Task<decimal> GetFreeAsync(ExchangeKey key, string coin, CancellationToken cancellationToken);

    /// <summary>Whether the account keeps a long and a short of the symbol apart (hedge mode) instead of one position.</summary>
    /// <exception cref="ProfitFox.Application.Exceptions.ExchangeKeyRejectedException">The exchange does not accept the key.</exception>
    /// <exception cref="ProfitFox.Application.Exceptions.ExchangeUnavailableException">The exchange did not answer.</exception>
    Task<bool> IsHedgeModeAsync(ExchangeKey key, Instrument instrument, CancellationToken cancellationToken);

    /// <summary>In <c>linear</c> the position of the symbol, a short below zero; on the spot what the account holds of the coin.</summary>
    /// <exception cref="ProfitFox.Application.Exceptions.ExchangeException">The exchange refused or did not answer.</exception>
    Task<decimal> GetHoldingAsync(ExchangeKey key, Instrument instrument, string baseCoin, CancellationToken cancellationToken);

    /// <summary>The leverage of the symbol of <c>linear</c> on the account, with a position or without.</summary>
    /// <exception cref="ProfitFox.Application.Exceptions.ExchangeException">The exchange refused or did not answer.</exception>
    Task<decimal> GetLeverageAsync(ExchangeKey key, Instrument instrument, CancellationToken cancellationToken);

    /// <summary>Every open order of the account on the instrument.</summary>
    /// <exception cref="ProfitFox.Application.Exceptions.ExchangeException">The exchange refused or did not answer.</exception>
    Task<IReadOnlyList<ExchangeOrder>> GetOpenOrdersAsync(ExchangeKey key, Instrument instrument, CancellationToken cancellationToken);

    /// <summary>The order the account placed with this client id, open or finished; <c>null</c> when the exchange does not know it.</summary>
    /// <exception cref="ProfitFox.Application.Exceptions.ExchangeException">The exchange refused or did not answer.</exception>
    Task<ExchangeOrder?> FindOrderAsync(ExchangeKey key, Instrument instrument, string clientId, CancellationToken cancellationToken);

    /// <summary>The order with this exchange's id, open or finished: the way to the stop or the target of an entry, which have no client id; <c>null</c> when the exchange does not know it.</summary>
    /// <exception cref="ProfitFox.Application.Exceptions.ExchangeException">The exchange refused or did not answer.</exception>
    Task<ExchangeOrder?> FindOrderByExchangeIdAsync(ExchangeKey key, Instrument instrument, string exchangeOrderId, CancellationToken cancellationToken);

    /// <summary>The orders of the instrument that name the order with this client id as their parent, open or finished in the recent history: the stop and the target of an entry.</summary>
    /// <exception cref="ProfitFox.Application.Exceptions.ExchangeException">The exchange refused or did not answer.</exception>
    Task<IReadOnlyList<ExchangeOrder>> GetOrdersOfParentAsync(ExchangeKey key, Instrument instrument, string parentClientId, CancellationToken cancellationToken);

    /// <summary>The executions of the order with this exchange's id, the oldest first.</summary>
    /// <exception cref="ProfitFox.Application.Exceptions.ExchangeException">The exchange refused or did not answer.</exception>
    Task<IReadOnlyList<ExchangeFill>> GetOrderFillsAsync(ExchangeKey key, Instrument instrument, string exchangeOrderId, CancellationToken cancellationToken);

    /// <summary>The executions of the order with this client id, the oldest first.</summary>
    /// <exception cref="ProfitFox.Application.Exceptions.ExchangeException">The exchange refused or did not answer.</exception>
    Task<IReadOnlyList<ExchangeFill>> GetFillsAsync(ExchangeKey key, Instrument instrument, string clientId, CancellationToken cancellationToken);

    /// <summary>The fee rates of the account on the instrument: maker for a limit order, taker for a market one.</summary>
    /// <exception cref="ProfitFox.Application.Exceptions.ExchangeException">The exchange refused or did not answer.</exception>
    Task<FeeSchedule> GetFeesAsync(ExchangeKey key, Instrument instrument, CancellationToken cancellationToken);
}