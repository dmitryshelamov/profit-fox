using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.Ports.LiveTrading;

/// <summary>Writes what live trading and the entries by ideas did to the service log; never a key, a secret or an amount of money of the account.</summary>
public interface ILiveTradingJournal
{
    void Took(Guid botId, long epoch);

    void Lost(Guid botId);

    void Sent(Guid botId, string clientId);

    void Refused(Guid botId, string clientId, string reason);

    void Failed(Guid botId, string reason);

    /// <summary>The exchange did not set the leverage of a symbol before an entry by an idea on the account.</summary>
    void LeverageRefused(Guid accountId, Instrument instrument, int leverage, string reason);
}