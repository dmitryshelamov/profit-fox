using ProfitFox.Domain.Entities.Accounts;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Accounts;

namespace ProfitFox.Tests.Unit.Application.Commands.Accounts;

/// <summary>
///     The accounts in memory, with the unique name without regard to case and the unique UID of the table, and the encrypted
///     secret of each.
/// </summary>
public sealed class MemoryExchangeAccountRepository : IExchangeAccountRepository
{
    private readonly List<(ExchangeAccount Account, ProtectedSecret Secret)> _rows = [];

    public int Writes { get; private set; }

    public Task<IReadOnlyList<ExchangeAccount>> AllAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<ExchangeAccount>>([.. _rows.Select(row => row.Account)]);
    }

    public Task<ExchangeAccount?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        return Task.FromResult(_rows.Select(row => row.Account).FirstOrDefault(account => account.Id == id));
    }

    public Task<bool> AddAsync(ExchangeAccount account, ProtectedSecret secret, CancellationToken cancellationToken)
    {
        if (_rows.Any(row => Same(row.Account.Name, account.Name) || row.Account.Uid == account.Uid))
        {
            return Task.FromResult(false);
        }

        Writes++;
        _rows.Add((account, secret));
        return Task.FromResult(true);
    }

    public Task<bool> RenameAsync(Guid id, string name, CancellationToken cancellationToken)
    {
        if (_rows.Any(row => row.Account.Id != id && Same(row.Account.Name, name)))
        {
            return Task.FromResult(false);
        }

        int index = _rows.FindIndex(row => row.Account.Id == id);
        if (index >= 0)
        {
            Writes++;
            _rows[index] = (_rows[index].Account with { Name = name }, _rows[index].Secret);
        }

        return Task.FromResult(true);
    }

    public Task<bool> SetMarginModeAsync(Guid id, string? marginMode, CancellationToken cancellationToken)
    {
        int index = _rows.FindIndex(row => row.Account.Id == id);
        if (index < 0)
        {
            return Task.FromResult(false);
        }

        Writes++;
        _rows[index] = (_rows[index].Account with { MarginMode = marginMode }, _rows[index].Secret);
        return Task.FromResult(true);
    }

    public Task SetFeedAsync(Guid id, string? feed, DateTimeOffset at, string worker, CancellationToken cancellationToken)
    {
        int index = _rows.FindIndex(row => row.Account.Id == id);
        if (index >= 0 && _rows[index].Account.Feed != feed)
        {
            _rows[index] = (_rows[index].Account with { Feed = feed, FeedChangedAt = at }, _rows[index].Secret);
        }

        return Task.CompletedTask;
    }

    public Task SetFillsCursorAsync(Guid id, DateTimeOffset at, string worker, CancellationToken cancellationToken)
    {
        int index = _rows.FindIndex(row => row.Account.Id == id);
        if (index >= 0)
        {
            _rows[index] = (_rows[index].Account with { FillsCursorAt = at }, _rows[index].Secret);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    ///     The accounts a trade of which holds a pair.
    /// </summary>
    public HashSet<Guid> Trading { get; } = [];

    public Task<bool?> DeleteIdleAsync(Guid id, CancellationToken cancellationToken)
    {
        return Trading.Contains(id) ? Task.FromResult<bool?>(null) : Task.FromResult<bool?>(_rows.RemoveAll(row => row.Account.Id == id) == 1);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        return Task.FromResult(_rows.RemoveAll(row => row.Account.Id == id) == 1);
    }

    public Task<ProtectedSecret?> SecretAsync(Guid id, CancellationToken cancellationToken)
    {
        return Task.FromResult<ProtectedSecret?>(_rows.Where(row => row.Account.Id == id).Select(row => row.Secret).FirstOrDefault());
    }

    /// <summary>
    ///     The encrypted secret as it is kept, for a test that looks at it.
    /// </summary>
    public ProtectedSecret Kept(Guid id)
    {
        return _rows.Single(row => row.Account.Id == id).Secret;
    }

    private static bool Same(string name, string other)
    {
        return string.Equals(name, other, StringComparison.OrdinalIgnoreCase);
    }
}
