namespace ProfitFox.Application.Transactions;

/// <summary>Runs a unit of work in one database transaction: its writes commit together or not at all.</summary>
public interface ITransactionExecutor
{
    /// <exception cref="InvalidOperationException">Called inside another unit of work.</exception>
    Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken);

    /// <summary>Guards a call that must not hold a transaction open, such as a call to the exchange.</summary>
    /// <exception cref="InvalidOperationException">Called inside a unit of work.</exception>
    void EnsureNoTransaction();
}