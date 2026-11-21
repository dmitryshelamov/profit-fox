using ProfitFox.Domain.ValueObjects.Ledger;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ledger.Contracts;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ledger.Converters;

internal static class FinishedRunDbConverter
{
    internal static FinishedRun ToDomain(FinishedRunDb row)
    {
        return new FinishedRun(LedgerEntryDbConverter.ToDomain(row), row.Parity);
    }
}
