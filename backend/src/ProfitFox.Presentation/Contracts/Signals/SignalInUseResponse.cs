using ProfitFox.Presentation.Contracts.Errors;

namespace ProfitFox.Presentation.Contracts.Signals;

/// <summary>
///     The answer to the delete of a signal a saved strategy points at (spec signal-constructor «Сигнал, на который ссылается
///     стратегия»): the error <c>SignalInUse</c> and the names of the strategies, in name order.
/// </summary>
public sealed record SignalInUseResponse(IReadOnlyList<RequestError> Errors, IReadOnlyList<string> Strategies);
