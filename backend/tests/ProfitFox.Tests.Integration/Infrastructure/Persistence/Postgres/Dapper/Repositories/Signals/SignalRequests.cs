using System.Text.Json;

using ProfitFox.Application.Commands.Signals.CreateSignal;
using ProfitFox.Application.Commands.Signals.DeleteSignal;
using ProfitFox.Application.Commands.Signals.RenameSignal;
using ProfitFox.Application.Commands.Signals.ReplaceSignal;
using ProfitFox.Application.Contracts.Signals;
using ProfitFox.Application.Queries.Signals;
using ProfitFox.Domain.ValueObjects.Signals;

namespace ProfitFox.Tests.Integration.Infrastructure.Persistence.Postgres.Dapper.Repositories.Signals;

/// <summary>
///     The requests of the signal library, sent through the sender of the test database.
/// </summary>
internal sealed class SignalRequests
{
    public Task<SignalInternal> Create(string name, string? description, JsonElement graph, IReadOnlyDictionary<string, NodePosition> layout, CancellationToken cancellationToken)
    {
        return BenchServices.Sender.Send(new CreateSignalCommand(name, description, graph, layout), cancellationToken);
    }

    public Task<SignalInternal> Replace(string name, string? description, JsonElement graph, IReadOnlyDictionary<string, NodePosition> layout, CancellationToken cancellationToken)
    {
        return BenchServices.Sender.Send(new ReplaceSignalCommand(name, description, graph, layout), cancellationToken);
    }

    public Task<SignalInternal> Rename(string name, string to, CancellationToken cancellationToken)
    {
        return BenchServices.Sender.Send(new RenameSignalCommand(name, to), cancellationToken);
    }

    public Task Delete(string name, CancellationToken cancellationToken)
    {
        return BenchServices.Sender.Send(new DeleteSignalCommand(name), cancellationToken);
    }

    public Task<SignalInternal> Read(string name, CancellationToken cancellationToken)
    {
        return BenchServices.Sender.Send(new GetSignalQuery(name), cancellationToken);
    }

    public Task<IReadOnlyList<SignalEntryInternal>> List(CancellationToken cancellationToken)
    {
        return BenchServices.Sender.Send(new GetSignalsQuery(), cancellationToken);
    }
}
