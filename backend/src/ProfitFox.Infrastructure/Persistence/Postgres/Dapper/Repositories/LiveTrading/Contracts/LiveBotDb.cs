namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.LiveTrading.Contracts;

internal sealed class LiveBotDb
{
    public Guid BotId { get; init; }

    public required string Status { get; init; }

    public string? StopReason { get; init; }

    public int? StrategyVersion { get; init; }

    public required string ParameterValues { get; init; }

    public bool StartPending { get; init; }

    public bool CloseRequested { get; init; }

    public int LastOrderNumber { get; init; }

    public DateTimeOffset? LastBarAt { get; init; }

    public int SessionFailures { get; init; }

    public string? SessionError { get; init; }

    public long LeaseEpoch { get; init; }

    /// <summary>Read only by the queries that ask for it.</summary>
    public DateTimeOffset UpdatedAt { get; init; }
}