namespace ProfitFox.Domain.ValueObjects.Ideas;

/// <summary>
///     The label a saved signal puts on ideas (change add-ideas-and-market, spec idea-labels «Метка сигнала»): whether it is
///     on, its text and the timeframe its signal is computed on.
/// </summary>
public sealed record SignalLabel(Guid SignalId, bool Enabled, string Text, TimeSpan Timeframe);
