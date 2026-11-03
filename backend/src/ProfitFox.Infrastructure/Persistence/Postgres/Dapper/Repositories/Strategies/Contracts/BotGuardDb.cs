namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Strategies.Contracts;

/// <summary>What a save or a delete of a stored bot must respect: whether it trades, holds an open position and what would change.</summary>
internal sealed class BotGuardDb
{
    public required string Status { get; init; }

    public bool Open { get; init; }

    public bool Changed { get; init; }

    public bool AccountChanged { get; init; }

    public bool CategoryChanged { get; init; }

    public bool SymbolChanged { get; init; }

    public bool IntervalChanged { get; init; }
}