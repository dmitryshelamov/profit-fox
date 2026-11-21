using MediatR;

using ProfitFox.Application.Contracts.Signals;

namespace ProfitFox.Application.Queries.Signals;

/// <summary>
///     Every saved signal in the order of its name; one that does not read stays in the list with its errors.
/// </summary>
public sealed record GetSignalsQuery : IRequest<IReadOnlyList<SignalEntryInternal>>;
