using ProfitFox.Engine.Strategy.Graph;

namespace ProfitFox.Engine.Strategy.Execution;

/// <summary>
///     The values of the declared outputs of the graph on the close of one candle of an instrument.
/// </summary>
public sealed record CloseResult(string InstrumentId, DateTimeOffset CloseTime, IReadOnlyDictionary<string, PinValue> Outputs);