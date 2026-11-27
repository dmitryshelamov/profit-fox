using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Tests.Unit.Application.Commands.Signals;

/// <summary>
///     A catalog of pairs for the tests: <c>BTCUSDT</c>, <c>ETHUSDT</c> and <c>SOLUSDT</c>.
/// </summary>
public sealed class ListedPairs : IInstrumentCatalog
{
    public Task<IReadOnlyList<Instrument>> LinearUsdtPerpetuals(CancellationToken cancellation)
    {
        return Task.FromResult<IReadOnlyList<Instrument>>([new Instrument("btc", "BTCUSDT"), new Instrument("eth", "ETHUSDT"), new Instrument("sol", "SOLUSDT")]);
    }
}
