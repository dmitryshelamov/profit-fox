using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.Contracts.MarketData.Converters;

public static class InstrumentInternalConverter
{
    public static InstrumentInternal ToInternal(this Instrument instrument)
    {
        return new InstrumentInternal(instrument.Id, instrument.Symbol);
    }
}
