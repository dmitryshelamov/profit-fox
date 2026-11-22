using System.Globalization;

using ProfitFox.Cli.Api;
using ProfitFox.Cli.Contracts.Backtests;

namespace ProfitFox.Cli.Commands.Runs;

/// <summary>
///     <c>bench runs</c>: the backtests that wait and go, then the last ones that ended.
/// </summary>
internal sealed class RunsCommand(BenchClient client, TextWriter output, TextWriter error)
{
    private const int FinishedLimit = 20;

    public async Task<int> Execute(CancellationToken cancellation)
    {
        Answer<BacktestDto[]> active = await client.Active(cancellation);
        Answer<BacktestDto[]> finished = await client.Finished(FinishedLimit, cancellation);
        if (active.Value is not { } going || finished.Value is not { } ended)
        {
            await error.WriteLineAsync($"cannot read the backtests: {active.Reason}{finished.Reason}");
            return 1;
        }

        await output.WriteLineAsync("active:");
        foreach (BacktestDto backtest in going)
        {
            string pairs = backtest.PairsTotal is { } total ? $"{backtest.PairsDone}/{total}" : $"{backtest.PairsDone}";
            await output.WriteLineAsync(
                $"  {backtest.Id}  {backtest.Strategy.Name} v{backtest.Strategy.Version}  {backtest.Strategy.Kind}  {backtest.SetCount} sets  {backtest.State}  {backtest.Worker ?? "-"}  {pairs}");
        }

        if (going.Length == 0)
        {
            await output.WriteLineAsync("  none");
        }

        await output.WriteLineAsync($"finished (last {FinishedLimit}):");
        foreach (BacktestDto backtest in ended)
        {
            await output.WriteLineAsync(
                $"  {backtest.Id}  {backtest.Strategy.Name} v{backtest.Strategy.Version}  {backtest.State}  {backtest.Settings.Start}..{backtest.Settings.End}  {backtest.SetCount} sets  best {Number(backtest.BestAmount)}");
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
