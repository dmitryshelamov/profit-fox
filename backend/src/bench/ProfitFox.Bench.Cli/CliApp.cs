namespace ProfitFox.Bench.Cli;

/// <summary>
///     The <c>bench</c> command line tool (spec bench-run-queue «Команды CLI»): a client of the REST API of the bench service.
///     Exit codes: 0 done, 1 refused or failed or the bench does not answer, 2 wrong command line.
/// </summary>
public static class CliApp
{
    /// <summary>
    ///     How often <c>--wait</c> asks the bench about the run.
    /// </summary>
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    ///     Runs one command.
    /// </summary>
    /// <param name="args">The command line.</param>
    /// <param name="output">Where results go.</param>
    /// <param name="error">Where refusals and mistakes go.</param>
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
        HttpClient? own = null;
        try
        {
            CommandLine line = CommandLine.Parse(args);
            if (http is null)
            {
                own = http = new HttpClient { BaseAddress = BenchUrl.Resolve(line.Value("--bench")), Timeout = TimeSpan.FromSeconds(60) };
            }

            var client = new BenchClient(http);
            TimeSpan interval = pollInterval ?? DefaultPollInterval;
            switch (line.Words[0])
            {
                case "run":
                    line.Allow("--version", "--period", "--wait");
                    return await new RunCommand(client, output, error, interval).Execute(line, parity: false, cancellation);
                case "parity":
                    line.Allow("--version", "--wait");
                    return await new RunCommand(client, output, error, interval).Execute(line, parity: true, cancellation);
                case "runs":
                    line.Allow();
                    return await new RunsCommand(client, output, error).Execute(line, cancellation);
                case "cancel":
                    line.Allow();
                    return await new CancelCommand(client, output, error).Execute(line, cancellation);
                case "hypotheses":
                    line.Allow();
                    return await new AddHypothesisCommand(client, output, error).Execute(line, cancellation);
                default:
                    throw new UsageException($"unknown command '{line.Words[0]}'.");
            }
        }
        catch (UsageException exception)
        {
            await error.WriteLineAsync(exception.Message);
            await error.WriteLineAsync(CliUsage.Text);
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
}
