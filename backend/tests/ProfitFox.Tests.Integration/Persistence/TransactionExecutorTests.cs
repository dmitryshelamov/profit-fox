using Dapper;

using Npgsql;

using ProfitFox.Infrastructure.Persistence.Postgres.Transactions;

using Shouldly;

namespace ProfitFox.Tests.Integration.Persistence;

/// <summary>Writes of one unit of work commit together or not at all.</summary>
public class TransactionExecutorTests
{
    private NpgsqlDataSource _dataSource = null!;
    private PostgresTransactionAccessor _accessor = null!;
    private PostgresTransactionExecutor _executor = null!;

    [SetUp]
    public async Task SetUp()
    {
        _dataSource = MarketDataDatabase.Open();
        await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync("CREATE TABLE notes (id int NOT NULL)");
        _accessor = new PostgresTransactionAccessor();
        _executor = new PostgresTransactionExecutor(_dataSource, _accessor);
    }

    [TearDown]
    public async Task TearDown()
    {
        // The reset before the next test empties only the tables of the migrations.
        await using (NpgsqlConnection connection = await _dataSource.OpenConnectionAsync())
        {
            await connection.ExecuteAsync("DROP TABLE notes");
        }

        await _dataSource.DisposeAsync();
    }

    [Test]
    public async Task Committed_writes_are_seen_together()
    {
        int written = await _executor.ExecuteAsync(
            async cancellationToken =>
            {
                await InsertAsync(1, cancellationToken);
                await InsertAsync(2, cancellationToken);

                return 2;
            },
            CancellationToken.None);

        written.ShouldBe(2);
        (await NotesAsync()).ShouldBe([1, 2]);
    }

    [Test]
    public async Task A_failure_leaves_no_write()
    {
        await Should.ThrowAsync<InvalidOperationException>(() => _executor.ExecuteAsync<int>(
            async cancellationToken =>
            {
                await InsertAsync(1, cancellationToken);

                throw new InvalidOperationException("Boom.");
            },
            CancellationToken.None));

        (await NotesAsync()).ShouldBeEmpty();
        Should.Throw<InvalidOperationException>(() => _accessor.Current);
    }

    // A nested unit would commit a part of the outer one on its own.
    [Test]
    public async Task A_unit_inside_a_unit_is_refused()
    {
        await Should.ThrowAsync<InvalidOperationException>(() => _executor.ExecuteAsync(
            _ => _executor.ExecuteAsync(_ => Task.FromResult(0), CancellationToken.None),
            CancellationToken.None));
    }

    // An exchange call inside a transaction would hold its row locks for the whole network round trip.
    [Test]
    public async Task No_transaction_is_ensured_only_outside_a_unit()
    {
        _executor.EnsureNoTransaction();

        await _executor.ExecuteAsync(
            _ =>
            {
                Should.Throw<InvalidOperationException>(_executor.EnsureNoTransaction);

                return Task.FromResult(0);
            },
            CancellationToken.None);
    }

    [Test]
    public void The_current_transaction_is_only_read_inside_a_unit()
    {
        Should.Throw<InvalidOperationException>(() => _accessor.Current);
    }

    private Task InsertAsync(int id, CancellationToken cancellationToken)
    {
        PostgresTransaction current = _accessor.Current;

        return current.Connection.ExecuteAsync(
            new CommandDefinition("INSERT INTO notes (id) VALUES (@Id)", new { Id = id }, current.Transaction, cancellationToken: cancellationToken));
    }

    private async Task<IReadOnlyList<int>> NotesAsync()
    {
        await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync();

        return [.. await connection.QueryAsync<int>("SELECT id FROM notes ORDER BY id")];
    }
}