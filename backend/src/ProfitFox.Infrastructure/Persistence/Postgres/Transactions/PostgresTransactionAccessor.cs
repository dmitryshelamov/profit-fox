namespace ProfitFox.Infrastructure.Persistence.Postgres.Transactions;

/// <summary>Scoped: the transaction a repository writes in. Set only while a unit of work runs.</summary>
internal sealed class PostgresTransactionAccessor
{
    private PostgresTransaction? _current;

    /// <exception cref="InvalidOperationException">No unit of work runs.</exception>
    public PostgresTransaction Current => _current ?? throw new InvalidOperationException("No unit of work runs.");

    internal bool HasCurrent => _current is not null;

    internal void Set(PostgresTransaction? current)
    {
        _current = current;
    }
}