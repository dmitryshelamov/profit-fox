using ProfitFox.Domain.ValueObjects.Signals;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Signals.Contracts;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Signals.Converters;

internal static class SignalDbConverter
{
    /// <summary>
    ///     The signal of a row with the graph of its latest version, or the errors that stop it from reading.
    /// </summary>
    internal static SignalRead ToDomain(SignalDb row)
    {
        return SignalJson.ReadStored(row.Name, row.Description, row.Graph, row.Layout, row.Version);
    }
}
