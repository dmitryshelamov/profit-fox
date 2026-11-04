namespace ProfitFox.Application.Scanner;

/// <summary>The <c>Scanner</c> section of the scanner's settings.</summary>
public sealed class ScannerSettings
{
    /// <summary>The only symbols the scanner keeps, in both categories; empty keeps every pair of the market.</summary>
    public IReadOnlyList<string> Symbols { get; init; } = [];

    /// <summary>How many days of history back from now the scanner loads and keeps.</summary>
    public int HistoryDays { get; init; } = 90;

    /// <summary>REST requests of the scanner per second, well below the limit Bybit sets for the IP, which the bots share.</summary>
    public int RestRequestsPerSecond { get; init; } = 20;

    /// <summary>How long the scanner sends no REST request after Bybit says it sends too many.</summary>
    public TimeSpan RestPause { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>How often the scanner reads the list of pairs again.</summary>
    public TimeSpan UniverseRefresh { get; init; } = TimeSpan.FromHours(1);

    /// <summary>How long after the end of a minute the scanner computes the snapshot, so the closed candles of the minute arrive first.</summary>
    public TimeSpan SnapshotDelay { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>How long the stream stays with the scanner that took it unless the scanner renews the lease.</summary>
    public TimeSpan LeaseDuration { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>How often the holder renews the lease of the stream; shorter than <see cref="LeaseDuration" />.</summary>
    public TimeSpan LeaseRenewal { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Minute candle topics on one connection of the stream.</summary>
    public int TopicsPerConnection { get; init; } = 100;
}