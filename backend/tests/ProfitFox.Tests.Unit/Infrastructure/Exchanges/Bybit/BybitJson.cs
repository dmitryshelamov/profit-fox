namespace ProfitFox.Tests.Unit.Infrastructure.Exchanges.Bybit;

/// <summary>Builds Bybit v5 response bodies for adapter tests.</summary>
public static class BybitJson
{
    /// <summary>Kline page with the given open times, newest first like Bybit returns them.</summary>
    public static string Kline(params string[] openTimes)
    {
        var items = openTimes.Select(time =>
            $"""["{DateTimeOffset.Parse(time).ToUnixTimeMilliseconds()}","1","1","1","1","1","1"]""");
        return $$"""{"retCode":0,"retMsg":"OK","result":{"list":[{{string.Join(",", items)}}]},"retExtInfo":{},"time":0}""";
    }

    public static string Error(int retCode, string retMsg) =>
        $$"""{"retCode":{{retCode}},"retMsg":"{{retMsg}}","result":{},"retExtInfo":{},"time":0}""";
}
