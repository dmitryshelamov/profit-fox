namespace ProfitFox.Domain.Entities.IdeaAutos;

/// <summary>
///     The request of an auto with money to enter by an idea, written by the scanner in the minute the idea was born in and taken by a trading
///     Worker: the account, the amount and the lowest reward to risk the auto had then, the place of the auto in the list (<see cref="Rank" />)
///     and the order of the idea among those of the minute (<see cref="Fired" />).
/// </summary>
public sealed record IdeaAutoEntry(
    Guid Id,
    Guid IdeaId,
    Guid AutoId,
    Guid AccountId,
    decimal Amount,
    decimal MinRiskReward,
    int Rank,
    int Fired,
    DateTimeOffset Minute,
    DateTimeOffset CreatedAt);