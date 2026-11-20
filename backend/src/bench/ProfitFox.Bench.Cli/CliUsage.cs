namespace ProfitFox.Bench.Cli;

/// <summary>
///     The text printed when the command line is wrong.
/// </summary>
internal static class CliUsage
{
    public const string Text =
        """
        usage:
          bench run <hypothesis> [--version N] [--period exploration|holdout|new-data] [--wait]
          bench parity <hypothesis> [--version N] [--wait]
          bench runs
          bench cancel <run id>
          bench hypotheses add <file>

        every command takes --bench <url> (default: Bench:Url of appsettings.json, else http://localhost:5240)
        """;
}
