using ProfitFox.Bench.Domain.Runs;

namespace ProfitFox.Bench.Application.Runs;

/// <summary>
///     What to run: a hypothesis on a period, blind for the parity check or with a verdict, optionally writing what was
///     read and computed into <see cref="DumpFolder" />.
/// </summary>
public sealed record RunOptions(string HypothesisFolder, Period Period, bool Parity, string? DumpFolder, DateTimeOffset Today);
