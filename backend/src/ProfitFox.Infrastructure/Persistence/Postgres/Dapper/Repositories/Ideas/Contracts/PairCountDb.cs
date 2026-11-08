namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas.Contracts;

/// <summary>The count of the distinct pairs with a signal of a side on an interval in a minute.</summary>
internal sealed class PairCountDb
{
    public DateTimeOffset FiredAt { get; init; }

    public required string Side { get; init; }

    public required string Interval { get; init; }

    public int Pairs { get; init; }
}