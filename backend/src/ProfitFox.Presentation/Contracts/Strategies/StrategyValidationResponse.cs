using ProfitFox.Presentation.Contracts.Errors;

namespace ProfitFox.Presentation.Contracts.Strategies;

/// <summary>
///     Every error of a graph checked as a strategy with its place, empty when there are none, and what each of its signal
///     nodes points at.
/// </summary>
public sealed record StrategyValidationResponse(IReadOnlyList<RequestError> Errors, IReadOnlyList<SignalUseResponse> Signals);
