namespace ProfitFox.Application.LiveTrading;

/// <summary>
///     Whether bots may be started on MOEX (decision 1): the <c>TInvest:Trading</c> setting of the API, off by default, so the code runs in
///     production with the trading off until the owner turns it on. A Worker goes on with the bots already started whatever it says.
/// </summary>
public sealed record MoexTradingSettings(bool Enabled);