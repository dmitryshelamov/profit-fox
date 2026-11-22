using ProfitFox.Cli.Api;

namespace ProfitFox.Cli.Commands.Runs;

/// <summary>
///     <c>bench cancel</c>: takes a waiting backtest out of the queue or asks the Worker to stop one that goes.
/// </summary>
internal sealed class CancelCommand(BenchClient client, TextWriter output, TextWriter error)
{
    public async Task<int> Execute(string run, CancellationToken cancellation)
    {
        Answer<object> answer = await client.Cancel(run, cancellation);
        if (answer.Refused)
        {
            await error.WriteLineAsync($"not cancelled: {answer.Reason}");
            return 1;
        }

        await output.WriteLineAsync($"cancelling backtest {run}");
        return 0;
    }
}
