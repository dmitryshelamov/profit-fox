using MediatR;

using ProfitFox.Application.Contracts.Signals;

namespace ProfitFox.Application.Queries.Signals;

/// <summary>
///     The latest version of the saved signal <see cref="Name" />.
/// </summary>
public sealed record GetSignalQuery(string Name) : IRequest<SignalInternal>;
