using ProfitFox.Application.Exceptions;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.Ports.MarketData;

/// <summary>What the exchange accepts for an instrument now: the steps of price and quantity and the order minimums.</summary>
public interface IInstrumentRulesProvider
{
    /// <exception cref="ExchangeUnavailableException">The exchange cannot answer for now.</exception>
    /// <exception cref="ExchangeException">The exchange rejected the request or does not list the instrument.</exception>
    Task<InstrumentRules> GetRulesAsync(Instrument instrument, CancellationToken cancellationToken);
}