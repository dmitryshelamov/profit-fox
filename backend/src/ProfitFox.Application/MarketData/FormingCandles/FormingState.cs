namespace ProfitFox.Application.MarketData.FormingCandles;

/// <summary>Where a feed of a series is: waiting for its first update, receiving updates, or without data (a lost connection, a refusal or silence).</summary>
public enum FormingState
{
    Connecting,
    Live,
    NoData
}