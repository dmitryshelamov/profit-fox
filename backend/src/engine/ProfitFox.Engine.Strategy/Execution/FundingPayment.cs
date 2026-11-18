namespace ProfitFox.Engine.Strategy.Execution;

/// <summary>
///     A funding payment of an instrument: the rate, a fraction and not a percent, the exchange settled at <paramref name="Time" />
///     (UTC). It is known from that moment.
/// </summary>
public sealed record FundingPayment(string InstrumentId, DateTimeOffset Time, decimal Rate);