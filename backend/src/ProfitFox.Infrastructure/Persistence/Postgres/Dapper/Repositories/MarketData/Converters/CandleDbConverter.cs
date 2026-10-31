using ProfitFox.Application.MarketData;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.MarketData.Contracts;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.MarketData.Converters;

internal static class CandleDbConverter
{
    internal static CandleDb FromDomain(Instrument instrument, Interval interval, Candle candle)
    {
        return new CandleDb
        {
            Category = instrument.Category.ToCode(),
            Symbol = instrument.Symbol,
            Interval = interval.ToCode(),
            OpenTime = candle.OpenTime,
            Open = candle.Open.Value,
            High = candle.High.Value,
            Low = candle.Low.Value,
            Close = candle.Close.Value,
            Volume = candle.Volume.Value,
            Turnover = candle.Turnover.Value
        };
    }

    internal static Candle ToDomain(CandleDb candle)
    {
        return new Candle(
            candle.OpenTime,
            new Price(candle.Open),
            new Price(candle.High),
            new Price(candle.Low),
            new Price(candle.Close),
            new Volume(candle.Volume),
            new Volume(candle.Turnover));
    }
}