using Microsoft.Extensions.DependencyInjection;

using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Domain.Entities.ExchangeAccounts.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Infrastructure.Exchanges.Routing;

/// <summary>The rules of an instrument from the exchange of its category (decision 8).</summary>
internal sealed class RoutedInstrumentRulesProvider(
    [FromKeyedServices(Exchange.Bybit)] IInstrumentRulesProvider bybit,
    [FromKeyedServices(Exchange.TInvest)] IInstrumentRulesProvider tinvest) : IInstrumentRulesProvider
{
    public Task<InstrumentRules> GetRulesAsync(Instrument instrument, CancellationToken cancellationToken)
    {
        return (instrument.Category.Exchange() == Exchange.TInvest ? tinvest : bybit).GetRulesAsync(instrument, cancellationToken);
    }
}