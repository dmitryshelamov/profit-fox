using ProfitFox.Bench.Domain.Signals;

namespace ProfitFox.Bench.Application.Signals;

/// <summary>
///     A saved signal as the store read it: the signal, or every error that stops it.
/// </summary>
public sealed record SignalRead(SignalDocument? Signal, IReadOnlyList<string> Errors);
