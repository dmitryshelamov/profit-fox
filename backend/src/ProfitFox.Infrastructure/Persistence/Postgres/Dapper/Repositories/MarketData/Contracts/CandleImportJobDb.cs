namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.MarketData.Contracts;

internal sealed class CandleImportJobDb
{
    public Guid Id { get; init; }

    public required string Category { get; init; }

    public required string Symbol { get; init; }

    public required string Interval { get; init; }

    public DateTimeOffset From { get; init; }

    public DateTimeOffset To { get; init; }

    public required string Status { get; init; }

    public long ImportedCandles { get; init; }

    public string? Error { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? StartedAt { get; init; }

    public DateTimeOffset? FinishedAt { get; init; }

    public int InterruptionsWithoutProgress { get; init; }
}