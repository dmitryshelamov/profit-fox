using Google.Protobuf.WellKnownTypes;

using ProfitFox.Infrastructure.Exchanges.TInvest;

using Tinkoff.InvestApi.V1;

namespace ProfitFox.Tests.Unit.Infrastructure.Exchanges.TInvest;

/// <summary>Shares and candles as T-Invest answers them.</summary>
internal static class TInvestFixtures
{
    public const string SberUid = "e6123145-9665-43e0-8413-cd61b8aa9b13";

    /// <summary>The trading schedule SBER keeps: the morning, the main and the evening sessions, the weekends and the dealer hours.</summary>
    public const string SberExchange = "moex_mrng_evng_e_wknd_dlr";

    public static Share Sber(string classCode = "TQBR", string currency = "rub", bool apiTrade = true, int lot = 10, decimal step = 0.01m)
    {
        return new Share
        {
            Uid = SberUid,
            Ticker = "SBER",
            ClassCode = classCode,
            Name = "Сбер Банк",
            Currency = currency,
            Lot = lot,
            MinPriceIncrement = step.ToQuotation(),
            ApiTradeAvailableFlag = apiTrade,
            ShortEnabledFlag = true,
            Exchange = SberExchange
        };
    }

    public static HistoricCandle Candle(DateTimeOffset open, decimal close, long lots, bool complete = true)
    {
        return new HistoricCandle
        {
            Time = Timestamp.FromDateTimeOffset(open),
            Open = (close - 1m).ToQuotation(),
            High = (close + 2m).ToQuotation(),
            Low = (close - 2m).ToQuotation(),
            Close = close.ToQuotation(),
            Volume = lots,
            IsComplete = complete
        };
    }
}