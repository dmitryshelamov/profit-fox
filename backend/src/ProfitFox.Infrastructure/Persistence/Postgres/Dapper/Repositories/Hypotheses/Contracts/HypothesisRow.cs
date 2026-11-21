namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Hypotheses.Contracts;

/// <summary>
///     A row of <c>hypothesis_versions</c>.
/// </summary>
internal sealed record HypothesisRow
{
    public string Id { get; init; } = "";

    public int Version { get; init; }

    public string Hash { get; init; } = "";

    public string Text { get; init; } = "";

    public DateTimeOffset CreatedAt { get; init; }
}
