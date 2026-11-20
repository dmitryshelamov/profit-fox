namespace ProfitFox.Bench.Cli;

/// <summary>
///     The command line is not one of the commands: <see cref="CliApp" /> prints what is wrong and the usage, and exits with 2.
/// </summary>
internal sealed class UsageException(string message) : Exception(message);
