namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ledger.Contracts;

/// <summary>
///     A row of <c>ledger</c> with the kind of its run in <c>runs</c>.
/// </summary>
internal sealed record FinishedRow : LedgerRow
{
    public bool Parity { get; init; }
}
