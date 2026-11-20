namespace ProfitFox.Bench.Cli;

/// <summary>
///     <c>bench cancel</c>: takes a waiting run out of the queue or asks the Worker to stop a run that goes.
/// </summary>
internal sealed class CancelCommand(BenchClient client, TextWriter output, TextWriter error)
{
    public async Task<int> Execute(CommandLine line, CancellationToken cancellation)
    {
        if (line.Words.Count != 2)
        {
            throw new UsageException("cancel wants one run id.");
        }

        Answer<object> answer = await client.Cancel(line.Words[1], cancellation);
        if (answer.Refused)
        {
            await error.WriteLineAsync($"not cancelled: {answer.Reason}");
            return 1;
        }

        await output.WriteLineAsync($"cancelling run {line.Words[1]}");
        return 0;
    }
}
