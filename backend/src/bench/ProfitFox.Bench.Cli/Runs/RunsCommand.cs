using System.Globalization;
using ProfitFox.Bench.Cli.Api;

namespace ProfitFox.Bench.Cli.Runs;

/// <summary>
///     <c>bench runs</c>: the runs that wait and go, then the last finished ones.
/// </summary>
internal sealed class RunsCommand(BenchClient client, TextWriter output, TextWriter error)
{
    private const int FinishedLimit = 20;

    public async Task<int> Execute(CancellationToken cancellation)
    {
        Answer<RunDto[]> active = await client.Active(cancellation);
        Answer<RunDto[]> finished = await client.Finished(FinishedLimit, cancellation);
        if (active.Value is not { } going || finished.Value is not { } ended)
        {
            await error.WriteLineAsync($"cannot read the runs: {active.Reason}{finished.Reason}");
            return 1;
        }

        await output.WriteLineAsync("active:");
        foreach (RunDto run in going)
        {
            string pairs = run.PairsTotal is { } total ? $"{run.PairsDone}/{total}" : $"{run.PairsDone}";
            await output.WriteLineAsync($"  {run.Id}  {run.Hypothesis} v{run.Version}  {run.Period}  {run.Kind}  {run.State}  {run.Worker ?? "-"}  {pairs}");
        }

        if (going.Length == 0)
        {
            await output.WriteLineAsync("  none");
        }

        await output.WriteLineAsync($"finished (last {FinishedLimit}):");
        foreach (RunDto run in ended)
        {
            await output.WriteLineAsync(
                $"  {run.Id}  {run.Hypothesis} v{run.Version}  {run.Period}  {run.Result?.Status ?? "-"}  power {Number(run.Result?.Power)}  t {Number(run.Result?.T)}");
        }

        if (ended.Length == 0)
        {
            await output.WriteLineAsync("  none");
        }

        return 0;
    }

    private static string Number(double? value)
    {
        return value?.ToString("0.##", CultureInfo.InvariantCulture) ?? "-";
    }
}
