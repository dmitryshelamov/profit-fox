namespace ProfitFox.Application.LiveTrading;

/// <summary>
///     The <c>Trading</c> section of the Worker's settings: whether this Worker trades, and the lease of the bots it keeps. Only a
///     trading Worker needs the master key.
/// </summary>
public sealed class TradingSettings
{
    public bool Enabled { get; init; }

    /// <summary>How long a bot stays with the Worker that took it unless the Worker renews the lease.</summary>
    public TimeSpan LeaseDuration { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>How often a Worker renews the leases of its bots; shorter than <see cref="LeaseDuration" />.</summary>
    public TimeSpan LeaseRenewal { get; init; } = TimeSpan.FromMinutes(1);
}