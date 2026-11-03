using System.Globalization;

using ProfitFox.Application.MarketData.FormingCandles;
using ProfitFox.Application.Ports.MarketData;

namespace ProfitFox.Presentation.Hubs;

/// <summary>The current candle as the hub sends it: prices and volume as decimal strings, time in UTC.</summary>
public sealed record FormingCandleMessage(DateTimeOffset OpenTime, string Open, string High, string Low, string Close, string Volume, bool IsClosed)
{
    public static FormingCandleMessage Of(FormingCandle candle)
    {
        return new FormingCandleMessage(
            candle.OpenTime,
            Format(candle.Open.Value),
            Format(candle.High.Value),
            Format(candle.Low.Value),
            Format(candle.Close.Value),
            Format(candle.Volume.Value),
            candle.IsClosed);
    }

    private static string Format(decimal value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }
}

/// <summary>What <c>Subscribe</c> answers: the state of the feed (<c>connecting</c>, <c>live</c> or <c>noData</c>) and its newest candle, <c>null</c> before the first.</summary>
public sealed record FormingSnapshotMessage(string State, FormingCandleMessage? Candle)
{
    public static FormingSnapshotMessage Of(FormingSnapshot snapshot)
    {
        return new FormingSnapshotMessage(StateName(snapshot.State), snapshot.Candle is { } candle ? FormingCandleMessage.Of(candle) : null);
    }

    public static string StateName(FormingState state)
    {
        return state switch
        {
            FormingState.Connecting => "connecting",
            FormingState.Live => "live",
            _ => "noData"
        };
    }
}