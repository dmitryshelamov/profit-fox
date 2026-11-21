using MediatR;

namespace ProfitFox.Application.Commands.Signals.DeleteSignal;

/// <summary>
///     Removes a saved signal with its versions; it must be there.
/// </summary>
public sealed record DeleteSignalCommand(string Name) : IRequest;
