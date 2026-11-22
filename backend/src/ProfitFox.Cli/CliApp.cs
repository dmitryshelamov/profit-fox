using System.CommandLine;
using System.CommandLine.Help;

using ProfitFox.Cli.Api;
using ProfitFox.Cli.Commands.Runs;
using ProfitFox.Cli.Contracts.Backtests;

namespace ProfitFox.Cli;

/// <summary>
///     The <c>bench</c> command line tool (spec bench-run-queue «Команды CLI»): a client of the REST API of the bench service.
///     The command line is parsed by System.CommandLine. Exit codes: 0 done, 1 refused or failed or the bench does not answer,
///     2 wrong command line.
/// </summary>
public static class CliApp
{
    /// <summary>
    ///     How often <c>--wait</c> asks the bench about the backtest.
    /// </summary>
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    ///     Runs one command.
    /// </summary>
    /// <param name="args">The command line.</param>
    /// <param name="output">Where results and the help go.</param>
    /// <param name="error">Where refusals and mistakes go, with the help of a wrong command line.</param>
    /// <param name="http">The client to talk to the bench with; made from <c>--bench</c> or <c>Bench:Url</c> when null.</param>
    /// <param name="pollInterval">How often <c>--wait</c> asks about the backtest; <see cref="DefaultPollInterval" /> when null.</param>
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

        var root = new Command("bench", "The client of the bench service: backtests of saved strategies.") { bench, new HelpOption { Recursive = true } };
        root.Subcommands.Add(Run((request, wait, token) => new RunCommand(Client(), output, error, interval).Execute(request, wait, token)));
        root.Subcommands.Add(Runs(token => new RunsCommand(Client(), output, error).Execute(token)));
        root.Subcommands.Add(Cancel((run, token) => new CancelCommand(Client(), output, error).Execute(run, token)));

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
    ///     <c>bench run</c> (change add-strategy-backtest, design.md, decision 11): a strategy by name and, as wanted, its
    ///     version, timeframes and pairs by commas, values of parameters as <c>name=v1,v2</c> as many times as there are
    ///     parameters, and the start and the end of history. What the values mean is checked by the bench.
    /// </summary>
    private static Command Run(Func<CreateBacktestDto, bool, CancellationToken, Task<int>> execute)
    {
        var strategy = new Argument<string>("strategy") { Description = "The name of the strategy, e.g. funding-short" };
        var version = new Option<int?>("--version") { Description = "The version of the strategy, by default its latest", HelpName = "N" };
        version.Validators.Add(result =>
        {
            // A value that is not a number is refused by the parser itself; reading it here would throw.
            if (result.Tokens is [var token] && int.TryParse(token.Value, out int number) && number <= 0)
            {
                result.AddError("--version wants a positive number.");
            }
        });
        var timeframe = new Option<string?>("--timeframe") { Description = "The timeframes by commas, of 1h, 4h and 1d; by default 4h", HelpName = "1h,4h" };
        var parameter = new Option<string[]>("--param") { Description = "Values of a parameter, as name=v1,v2; once for each parameter", HelpName = "name=v1,v2" };
        parameter.Validators.Add(result =>
        {
            foreach (string token in result.Tokens.Select(token => token.Value).Where(token => !Values(token, out _, out _)))
            {
                result.AddError($"--param wants name=v1,v2, not {token}.");
            }
        });
        var from = new Option<string?>("--from") { Description = "The start of history, a date UTC such as 2025-01-01", HelpName = "date" };
        var until = new Option<string?>("--until") { Description = "The end of history, not included; by default the start of today UTC", HelpName = "date" };
        var pairs = new Option<string?>("--pairs") { Description = "The pairs by commas; by default every listed pair", HelpName = "A,B" };
        var wait = new Option<bool>("--wait") { Description = "Follow the backtest to its end and print the table of its sets" };

        var command = new Command("run", "Queues a backtest of a strategy.") { strategy, version, timeframe, parameter, from, until, pairs, wait };
        command.SetAction((result, token) =>
        {
            Dictionary<string, string[]> values = [];
            foreach (string text in result.GetValue(parameter) ?? [])
            {
                Values(text, out string name, out string[] list);
                values[name] = list;
            }

            var request = new CreateBacktestDto(
                result.GetRequiredValue(strategy),
                result.GetValue(version),
                values.Count == 0 ? null : values,
                List(result.GetValue(timeframe)),
                result.GetValue(from),
                result.GetValue(until),
                List(result.GetValue(pairs)));
            return execute(request, result.GetValue(wait), token);
        });
        return command;
    }

    // name=v1,v2: the name and its values, each value without its spaces.
    private static bool Values(string text, out string name, out string[] values)
    {
        int equals = text.IndexOf('=', StringComparison.Ordinal);
        name = equals > 0 ? text[..equals].Trim() : "";
        values = equals > 0 ? List(text[(equals + 1)..]) ?? [] : [];
        return name.Length > 0 && values.Length > 0;
    }

    private static string[]? List(string? text)
    {
        string[] items = text is null ? [] : [.. text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
        return items.Length == 0 ? null : items;
    }

    private static Command Runs(Func<CancellationToken, Task<int>> execute)
    {
        var command = new Command("runs", "Prints the backtests that wait and go, then the last ones that ended.");
        command.SetAction((_, token) => execute(token));
        return command;
    }

    private static Command Cancel(Func<string, CancellationToken, Task<int>> execute)
    {
        var run = new Argument<string>("backtest") { Description = "The id of the backtest" };
        var command = new Command("cancel", "Takes a waiting backtest out of the queue or stops one that goes.") { run };
        command.SetAction((result, token) => execute(result.GetRequiredValue(run), token));
        return command;
    }
}