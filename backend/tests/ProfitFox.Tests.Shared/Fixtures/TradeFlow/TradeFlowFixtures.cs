namespace ProfitFox.Tests.Shared.Fixtures.TradeFlow;

/// <summary>
///     Reference files of the daily archive of trades of Bybit, SOLUSDT on 2026-11-09, as they are before compression: <c>linear</c> (time in
///     seconds with a fraction, <c>Buy</c> / <c>Sell</c>, <c>size</c>) and <c>spot</c> (time in milliseconds, <c>buy</c> / <c>sell</c>,
///     <c>volume</c>). Both hold the same five trades: in 12:04 buys of 2 at 150 and 1 at 151 and a sell of 3 at 149 at 12:04:59.999; in
///     12:05 a sell of 1 at 150 at 12:05:00.000 and a buy of 0.5 at 150.5, a trade of the RPI orders.
/// </summary>
public static class TradeFlowFixtures
{
    public static byte[] Day(string category)
    {
        using Stream stream = typeof(TradeFlowFixtures).Assembly.GetManifestResourceStream($"ProfitFox.Tests.Shared.Fixtures.TradeFlow.{category}-SOLUSDT-2026-11-09.csv")!;
        using var copy = new MemoryStream();
        stream.CopyTo(copy);

        return copy.ToArray();
    }
}