namespace ProfitFox.Application.LiveTrading;

/// <summary>The <c>Trading</c> section of the Worker's settings: whether this Worker trades. Only a trading Worker needs the master key.</summary>
public sealed class TradingSettings
{
    public bool Enabled { get; init; }
}