using ProfitFox.Bench.Domain.Runs;

namespace ProfitFox.Bench.Cli;

/// <summary>
///     A command of the <c>bench</c> tool: a run of a hypothesis on a period, with a verdict or blind for the parity check.
/// </summary>
public sealed record BenchCommand(string Folder, Period Period, bool Parity, string? DumpFolder);
