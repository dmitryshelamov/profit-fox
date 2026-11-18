using ProfitFox.Bench.Runs;

namespace ProfitFox.Bench.Cli;

/// <summary>
///     The command line of <c>bench</c> (spec hypothesis-bench):
///     <c>bench run &lt;folder&gt; [--period exploration|holdout|new-data] [--parity-dump &lt;folder&gt;]</c> and
///     <c>bench parity &lt;folder&gt; --out &lt;folder&gt;</c>.
/// </summary>
public static class CommandLine
{
    public const string Usage =
        """
        usage: bench run <hypothesis folder> [--period exploration|holdout|new-data] [--parity-dump <folder>]
               bench parity <hypothesis folder> --out <folder>
        """;

    public static BenchCommand? Parse(IReadOnlyList<string> args, out string? error)
    {
        error = null;
        if (args.Count < 2 || args[0] is not ("run" or "parity"))
        {
            error = "a command and a hypothesis folder are needed";
            return null;
        }

        bool parity = args[0] == "parity";
        Period period = Period.Exploration;
        string? dump = null;
        for (int index = 2; index < args.Count; index++)
        {
            string option = args[index];
            string? value = index + 1 < args.Count ? args[index + 1] : null;
            switch (option)
            {
                case "--period" when !parity && value is not null && Periods.TryParse(value, out Period parsed):
                    period = parsed;
                    index++;
                    break;
                case "--parity-dump" when !parity && value is not null:
                case "--out" when parity && value is not null:
                    dump = value;
                    index++;
                    break;
                default:
                    error = $"unknown or incomplete option {option}";
                    return null;
            }
        }

        if (parity && dump is null)
        {
            error = "bench parity needs --out <folder>";
            return null;
        }

        return new BenchCommand(args[1], period, parity, dump);
    }
}
