namespace ProfitFox.Bench.Cli.Api;

/// <summary>
///     The address of the bench from <c>--bench</c> or <c>Bench:Url</c> is not an http address: <see cref="CliApp" /> prints it
///     and exits with 2.
/// </summary>
internal sealed class UsageException(string message) : Exception(message);
