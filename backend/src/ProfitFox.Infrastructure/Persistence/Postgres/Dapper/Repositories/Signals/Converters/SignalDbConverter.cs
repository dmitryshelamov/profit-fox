using System.Text.Json;

using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.ValueObjects.Graph;
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
        return SignalJson.ReadStored(row.Id, row.Name, row.Description, row.Graph, row.Layout, row.Version);
    }

    /// <summary>
    ///     The version of a row; its graph is read only by its form, the checks of a signal are the job of the set of signals.
    /// </summary>
    internal static SignalVersion ToDomain(SignalVersionDb row)
    {
        GraphDefinition? graph = null;
        if (row.Graph is not null)
        {
            using var document = JsonDocument.Parse(row.Graph);
            graph = StrategyGraph.ReadForm(document.RootElement).Value;
        }

        return new SignalVersion(new SignalReference(row.Signal, row.Version), row.Name, row.LatestVersion, graph);
    }
}
