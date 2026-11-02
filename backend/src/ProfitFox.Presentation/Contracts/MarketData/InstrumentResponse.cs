using ProfitFox.Application.MarketData;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Presentation.Contracts.MarketData;

public sealed record InstrumentResponse(string Category, string Symbol, string BaseCoin, string QuoteCoin)
{
    public static InstrumentResponse Of(ListedInstrument instrument)
    {
        return new InstrumentResponse(instrument.Category.ToCode(), instrument.Symbol, instrument.BaseCoin, instrument.QuoteCoin);
    }
}