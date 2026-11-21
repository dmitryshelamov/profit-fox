using ProfitFox.Domain.ValueObjects.Hypotheses;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Hypotheses.Contracts;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Hypotheses.Converters;

internal static class HypothesisDbConverter
{
    internal static StoredHypothesis ToDomain(HypothesisDb row)
    {
        return new StoredHypothesis(row.Id, row.Version, row.Hash, row.Text, row.CreatedAt);
    }

    /// <summary>
    ///     The version and the hash of a row read without its text.
    /// </summary>
    internal static HypothesisKey ToKey(HypothesisDb row)
    {
        return new HypothesisKey(row.Id, row.Version, row.Hash);
    }
}
