using MediatR;

using ProfitFox.Application.Contracts.Runs;

namespace ProfitFox.Application.Queries.Runs;

/// <summary>
///     The run <see cref="RunId" /> by its row in the queue and, once it ended, by its line in the ledger; null when the queue
///     has no such run.
/// </summary>
public sealed record GetRunQuery(string RunId) : IRequest<RunDetailsInternal?>;
