using Npgsql;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Transactions;

/// <summary>The connection and the transaction of the running unit of work.</summary>
internal sealed record PostgresTransaction(NpgsqlConnection Connection, NpgsqlTransaction Transaction);