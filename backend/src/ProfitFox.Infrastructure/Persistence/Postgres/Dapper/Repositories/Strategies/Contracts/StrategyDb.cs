namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Strategies.Contracts;

/// <summary>
///     A row of <c>strategies</c> with the graph of its latest version.
/// </summary>
internal sealed record StrategyDb
{
    public Guid Id { get; init; }

    public string Name { get; init; } = "";

    public string? Description { get; init; }

    public string Layout { get; init; } = "{}";

    public int Version { get; init; }

    public string Graph { get; init; } = "{}";
}
