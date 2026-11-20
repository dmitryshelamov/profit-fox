namespace ProfitFox.Bench.Cli;

/// <summary>
///     The entry point of the <c>bench</c> command line tool: everything it does is in <see cref="CliApp" />.
/// </summary>
internal static class Program
{
    private static Task<int> Main(string[] args)
    {
        return CliApp.Run(args, Console.Out, Console.Error);
    }
}
