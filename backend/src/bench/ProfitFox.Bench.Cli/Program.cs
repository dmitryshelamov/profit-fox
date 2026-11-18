namespace ProfitFox.Bench.Cli;

/// <summary>
///     The `bench` command line tool.
/// </summary>
internal static class Program
{
    /// <summary>
    ///     The commands of the bench come with the run (spec hypothesis-bench); until then the tool only says how it is
    ///     used.
    /// </summary>
    private static int Main(string[] args)
    {
        Console.Error.WriteLine("usage: bench run <hypothesis folder> [--period exploration|holdout|new-data]");

        return 2;
    }
}