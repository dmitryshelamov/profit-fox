using System.CommandLine;
using System.CommandLine.Help;
using ProfitFox.Bench.Cli.Api;
using ProfitFox.Bench.Cli.Hypotheses;
using ProfitFox.Bench.Cli.Runs;

namespace ProfitFox.Bench.Cli;

/// <summary>
///     The <c>bench</c> command line tool (spec bench-run-queue «Команды CLI»): a client of the REST API of the bench service.
///     The command line is parsed by System.CommandLine. Exit codes: 0 done, 1 refused or failed or the bench does not answer,
///     2 wrong command line.
/// </summary>
public static class CliApp
{
    /// <summary>
    ///     How often <c>--wait</c> asks the bench about the run.
    /// </summary>
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(5);

    private static readonly string[] Periods = ["exploration", "holdout", "new-data"];

    /// <summary>
    ///     Runs one command.
    /// </summary>
    /// <param name="args">The command line.</param>
    /// <param name="output">Where results and the help go.</param>
    /// <param name="error">Where refusals and mistakes go, with the help of a wrong command line.</param>
    /// <param name="http">The client to talk to the bench with; made from <c>--bench</c> or <c>Bench:Url</c> when null.</param>
    /// <param name="pollInterval">How often <c>--wait</c> asks about the run; <see cref="DefaultPollInterval" /> when null.</param>
    /// <param name="cancellation">Stops the command.</param>
    /// <returns>The exit code.</returns>
    public static async Task<int> Run(
        string[] args,
        TextWriter output,
        TextWriter error,
        HttpClient? http = null,
        TimeSpan? pollInterval = null,
        CancellationToken cancellation = default)
    {
        BenchClient? client = null;
        TimeSpan interval = pollInterval ?? DefaultPollInterval;
        BenchClient Client()
        {
            return client ?? throw new InvalidOperationException("The client is made before a command runs.");
        }

        var bench = new Option<string?>("--bench")
        {
            Description = "The address of the bench, by default Bench:Url of appsettings.json, else " + BenchUrl.Default,
            HelpName = "url",
            Recursive = true
        };

        var root = new Command("bench", "The client of the hypothesis bench service.") { bench, new HelpOption { Recursive = true } };
        root.Subcommands.Add(Run(parity: false, (hypothesis, version, period, wait, token) =>
            new RunCommand(Client(), output, error, interval).Execute(hypothesis, version, period, wait, parity: false, token)));
        root.Subcommands.Add(Run(parity: true, (hypothesis, version, _, wait, token) =>
            new RunCommand(Client(), output, error, interval).Execute(hypothesis, version, "exploration", wait, parity: true, token)));
        root.Subcommands.Add(Runs(token => new RunsCommand(Client(), output, error).Execute(token)));
        root.Subcommands.Add(Cancel((run, token) => new CancelCommand(Client(), output, error).Execute(run, token)));
        root.Subcommands.Add(Hypotheses((file, token) => new AddHypothesisCommand(Client(), output, error).Execute(file, token)));

        ParseResult parse = root.Parse(args);
        if (parse.Errors.Count > 0)
        {
            // The errors and the help of the command both go to the error writer.
            await parse.InvokeAsync(new InvocationConfiguration { Output = error, Error = error }, cancellation);
            return 2;
        }

        HttpClient? own = null;
        try
        {
            if (http is null)
            {
                own = http = new HttpClient { BaseAddress = BenchUrl.Resolve(parse.GetValue(bench)), Timeout = TimeSpan.FromSeconds(60) };
            }

            client = new BenchClient(http);
            return await parse.InvokeAsync(new InvocationConfiguration { Output = output, Error = error, EnableDefaultExceptionHandler = false }, cancellation);
        }
        catch (UsageException exception)
        {
            await error.WriteLineAsync(exception.Message);
            return 2;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellation.IsCancellationRequested)
        {
            await error.WriteLineAsync($"the bench does not answer: {exception.Message}");
            return 1;
        }
        finally
        {
            own?.Dispose();
        }
    }

    /// <summary>
    ///     <c>bench run</c> with a verdict, or <c>bench parity</c>, a blind run on exploration only.
    /// </summary>
    private static Command Run(bool parity, Func<string, int?, string, bool, CancellationToken, Task<int>> execute)
    {
        var hypothesis = new Argument<string>("hypothesis") { Description = "The id of the hypothesis, e.g. H0001" };
        var version = new Option<int?>("--version") { Description = "The version of the hypothesis, by default its latest", HelpName = "N" };
        version.Validators.Add(result =>
        {
            // A value that is not a number is refused by the parser itself; reading it here would throw.
            if (result.Tokens is [var token] && int.TryParse(token.Value, out int number) && number <= 0)
            {
                result.AddError("--version wants a positive number.");
            }
        });
        var period = new Option<string>("--period") { Description = "The period of the run", DefaultValueFactory = _ => "exploration" };
        period.AcceptOnlyFromAmong(Periods);
        var wait = new Option<bool>("--wait") { Description = "Follow the run to its end and print the verdict" };

        Command command = parity
            ? new Command("parity", "Queues a blind parity run of a hypothesis on exploration.") { hypothesis, version, wait }
            : new Command("run", "Queues a run of a hypothesis with a verdict.") { hypothesis, version, period, wait };
        command.SetAction((result, token) =>
            execute(result.GetRequiredValue(hypothesis), result.GetValue(version), result.GetValue(period) ?? "exploration", result.GetValue(wait), token));
        return command;
    }

    private static Command Runs(Func<CancellationToken, Task<int>> execute)
    {
        var command = new Command("runs", "Prints the runs that wait and go, then the last finished ones.");
        command.SetAction((_, token) => execute(token));
        return command;
    }

    private static Command Cancel(Func<string, CancellationToken, Task<int>> execute)
    {
        var run = new Argument<string>("run") { Description = "The id of the run" };
        var command = new Command("cancel", "Takes a waiting run out of the queue or stops a run that goes.") { run };
        command.SetAction((result, token) => execute(result.GetRequiredValue(run), token));
        return command;
    }

    private static Command Hypotheses(Func<string, CancellationToken, Task<int>> execute)
    {
        var file = new Argument<string>("file") { Description = "The hypothesis.json to load" };
        var add = new Command("add", "Loads a hypothesis or its new version from the text of a hypothesis.json.") { file };
        add.SetAction((result, token) => execute(result.GetRequiredValue(file), token));
        return new Command("hypotheses", "The hypotheses of the bench.") { add };
    }
}
