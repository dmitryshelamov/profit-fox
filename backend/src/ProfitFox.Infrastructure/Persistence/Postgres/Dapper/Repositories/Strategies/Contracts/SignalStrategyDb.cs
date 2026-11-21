namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Strategies.Contracts;

/// <summary>
///     A signal and the name of a strategy a version of which points at it.
/// </summary>
internal sealed record SignalStrategyDb
{
    public Guid Signal { get; init; }

    public string Name { get; init; } = "";
}
