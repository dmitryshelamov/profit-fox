using System.Globalization;

using ProfitFox.Cli.Api;

namespace ProfitFox.Cli.Runs;

/// <summary>
///     <c>bench run</c> and <c>bench parity</c>: puts a run in the queue and prints its id; with <c>--wait</c> follows it to its end
///     and prints the verdict.
/// </summary>
internal sealed class RunCommand(BenchClient client, TextWriter output, TextWriter error, TimeSpan pollInterval)
{
    public async Task<int> Execute(string hypothesis, int? version, string period, bool wait, bool parity, CancellationToken cancellation)
    {
        Answer<RunDto> queued = await client.Enqueue(new CreateRunDto(hypothesis, version, period, parity ? "parity" : "verdict"), cancellation);
        if (queued.Value is not { } run)
        {
            await error.WriteLineAsync($"refused: {queued.Reason}");
            return 1;
        }

        await output.WriteLineAsync($"queued {run.Kind} run {run.Id} of {run.Hypothesis} v{run.Version} on {run.Period}");
        return wait ? await Wait(run, cancellation) : 0;
    }

    private async Task<int> Wait(RunDto queued, CancellationToken cancellation)
    {
        (string State, int PairsDone)? shown = null;
        while (true)
        {
            Answer<RunDto> answer = await client.Find(queued.Id, cancellation);
            if (answer.Value is not { } run)
            {
                await error.WriteLineAsync($"cannot read run {queued.Id}: {answer.Reason}");
                return 1;
            }

            if (run.State == "finished")
            {
                return await Finish(run);
            }

            if (shown != (run.State, run.PairsDone))
            {
                shown = (run.State, run.PairsDone);
                string pairs = run.PairsTotal is { } total ? $"{run.PairsDone}/{total}" : $"{run.PairsDone}";
                await output.WriteLineAsync($"{run.State}: {pairs} pairs");
            }

            await Task.Delay(pollInterval, cancellation);
        }
    }

    private async Task<int> Finish(RunDto run)
    {
        if (run.Result is not { } result)
        {
            await error.WriteLineAsync($"run {run.Id} ended without a result.");
            return 1;
        }

        await output.WriteLineAsync($"{result.Status}: {result.Reason}");
        List<string> numbers = [];
        if (result.T is { } t)
        {
            numbers.Add($"t {Number(t)}");
        }

        if (result.RequiredT is { } required)
        {
            numbers.Add($"required t {Number(required)}");
        }

        if (result.Power is { } power)
        {
            numbers.Add($"power {Number(power)}");
        }

        if (numbers.Count > 0)
        {
            await output.WriteLineAsync(string.Join(", ", numbers));
        }

        await output.WriteLineAsync($"run page: /runs/{run.Id}");
        return result.Status is "failed" or "cancelled" ? 1 : 0;
    }

    private static string Number(double value)
    {
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
