using Grpc.Net.Client;
using ProfitFox.Bench.Application.Hypotheses;
using ProfitFox.Bench.Application.MarketData;
using ProfitFox.Bench.Application.Runs;
using ProfitFox.Bench.Domain.Hypotheses;
using ProfitFox.Bench.Domain.Ledger;
using ProfitFox.Bench.Infrastructure.Hypotheses;
using ProfitFox.Bench.Infrastructure.Ledger;
using ProfitFox.Bench.Infrastructure.MarketData;
using ProfitFox.Bench.Infrastructure.Runs;

namespace ProfitFox.Bench.Cli;

/// <summary>
///     The <c>bench</c> command line tool: runs a hypothesis and prints its verdict and the path of its report. Exit codes:
///     0 a run with its line in the ledger, 1 a refused or failed run, 2 a wrong command line.
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        BenchCommand? command = CommandLine.Parse(args, out string? error);
        if (command is null)
        {
            Console.Error.WriteLine(error);
            Console.Error.WriteLine(CommandLine.Usage);
            return 2;
        }

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, press) =>
        {
            press.Cancel = true;
            cancellation.Cancel();
        };

        try
        {
            string root = RepositoryRoot(Directory.GetCurrentDirectory());
            BenchSettings settings = BenchSettings.Load(AppContext.BaseDirectory);
            using GrpcChannel channel = GrpcChannel.ForAddress(settings.GrpcUrl, new GrpcChannelOptions { MaxReceiveMessageSize = 64 * 1024 * 1024 });
            using var http = new HttpClient { BaseAddress = new Uri(settings.RestUrl), Timeout = TimeSpan.FromMinutes(1) };
            string bench = Path.Combine(root, "bench");
            var runner = new HypothesisRunner(
                new HypothesisCatalog(new HypothesisFiles(Path.Combine(bench, "hypotheses"))),
                new GrpcHistorySource(channel),
                new InstrumentCatalog(http),
                new GitRepository(root),
                new LedgerFile(Path.Combine(bench, "ledger.jsonl")),
                new ReportFiles(bench),
                new ParityDumpFiles(),
                Console.Out);
            var options = new RunOptions(
                Path.GetFullPath(command.Folder),
                command.Period,
                command.Parity,
                command.DumpFolder is null ? null : Path.GetFullPath(command.DumpFolder),
                new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero));

            RunResult result = await runner.Run(options, cancellation.Token);
            Console.WriteLine($"{result.Entry.Hypothesis} {result.Entry.Period}: {result.Entry.Status} — {result.Entry.Reason}");
            Console.WriteLine($"report: {Path.GetRelativePath(root, result.ReportPath)}");
            return 0;
        }
        catch (Exception exception) when (exception is HypothesisException or LedgerException or UncommittedChangesException
                                              or HistoryUnavailableException or PeriodBoundaryException or InvalidOperationException)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("stopped");
            return 1;
        }
    }

    private static string RepositoryRoot(string start)
    {
        DirectoryInfo? folder = new DirectoryInfo(start);
        while (folder is not null && !Path.Exists(Path.Combine(folder.FullName, ".git")))
        {
            folder = folder.Parent;
        }

        return folder?.FullName ?? throw new InvalidOperationException("bench runs inside the profit-fox repository.");
    }
}
