namespace ProfitFox.Application.TradeFlow;

/// <summary>The <c>TradeFlow</c> section of the settings of the collector.</summary>
public sealed class TradeFlowSettings
{
    /// <summary>How many closed days UTC back from today the collector rebuilds from the daily archive.</summary>
    public int HistoryDays { get; init; } = 30;

    /// <summary>The address of the daily archive of trades; the tests point it at a stub.</summary>
    public string ArchiveAddress { get; init; } = "https://public.bybit.com";

    /// <summary>The time of the next day UTC before which the archive of a day is not asked for.</summary>
    public TimeSpan ArchiveNotBefore { get; init; } = new TimeSpan(2, 30, 0);

    /// <summary>How long the trade flow stays with the collector that took it unless the collector renews the lease.</summary>
    public TimeSpan LeaseDuration { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>How often the holder renews the lease; shorter than <see cref="LeaseDuration" />.</summary>
    public TimeSpan LeaseRenewal { get; init; } = TimeSpan.FromSeconds(10);
}