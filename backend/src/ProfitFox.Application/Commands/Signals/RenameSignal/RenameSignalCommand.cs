using MediatR;

using ProfitFox.Application.Contracts.Signals;

namespace ProfitFox.Application.Commands.Signals.RenameSignal;

/// <summary>
///     Gives a saved signal the name <see cref="To" /> and keeps its versions.
/// </summary>
public sealed record RenameSignalCommand(string Name, string To) : IRequest<SignalInternal>;
