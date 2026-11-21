namespace ProfitFox.Domain.Execution;

/// <summary>
///     A funding payment of a pair: the moment it was paid and its rate.
/// </summary>
public sealed record FundingPayment(DateTimeOffset Time, decimal Rate);
