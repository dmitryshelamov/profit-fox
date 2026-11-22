using System.Globalization;

using ProfitFox.Cli.Api;
using ProfitFox.Cli.Contracts.Backtests;

namespace ProfitFox.Cli.Commands.Runs;

/// <summary>
///     <c>bench run</c> (spec bench-run-queue «Команды CLI»): puts a backtest of a strategy in the queue and prints its id;
///     with <c>--wait</c> follows it to its end and prints the table of its sets.
/// </summary>
internal sealed class RunCommand(BenchClient client, TextWriter output, TextWriter error, TimeSpan pollInterval)
{
    private static readonly string[] States = ["done", "failed", "cancelled"];

    public async Task<int> Execute(CreateBacktestDto request, bool wait, CancellationToken cancellation)
    {
        Answer<BacktestDto> queued = await client.Enqueue(request, cancellation);
        if (queued.Value is not { } backtest)
        {
            await error.WriteLineAsync($"refused: {queued.Reason}");
            return 1;
        }

        await output.WriteLineAsync($"queued backtest {backtest.Id} of {backtest.Strategy.Name} v{backtest.Strategy.Version}: {Sets(backtest.SetCount)}");
        return wait ? await Wait(backtest, cancellation) : 0;
    }

    private async Task<int> Wait(BacktestDto queued, CancellationToken cancellation)
    {
        (string State, int PairsDone)? shown = null;
        while (true)
        {
            Answer<BacktestDto> answer = await client.Find(queued.Id, cancellation);
            if (answer.Value is not { } backtest)
            {
                await error.WriteLineAsync($"cannot read backtest {queued.Id}: {answer.Reason}");
                return 1;
            }

            if (States.Contains(backtest.State))
            {
                return await Finish(backtest);
            }

            if (shown != (backtest.State, backtest.PairsDone))
            {
                shown = (backtest.State, backtest.PairsDone);
                string pairs = backtest.PairsTotal is { } total ? $"{backtest.PairsDone}/{total}" : $"{backtest.PairsDone}";
                await output.WriteLineAsync($"{backtest.State}: {pairs} pairs");
            }

            await Task.Delay(pollInterval, cancellation);
        }
    }

    // The table of the sets of a done backtest, or the state and the reason of one that ended without them.
    private async Task<int> Finish(BacktestDto backtest)
    {
        if (backtest.State != "done")
        {
            await output.WriteLineAsync($"{backtest.State}: {backtest.Reason}");
            await output.WriteLineAsync($"run page: /runs/{backtest.Id}");
            return 1;
        }

        await output.WriteLineAsync($"done: {backtest.Settings.Start}..{backtest.Settings.End}, {backtest.PairsDone} pairs");
        await output.WriteLineAsync("set  timeframe  values  trades  mean  sum USDT  wins  difference  t");
        foreach (BacktestSetDto set in backtest.Sets ?? [])
        {
            SetNumbersDto? numbers = set.Numbers;
            string values = set.Values.Count == 0 ? "-" : string.Join(" ", set.Values.Select(value => $"{value.Name}={value.Value}"));
            await output.WriteLineAsync(string.Join(
                "  ",
                set.Number.ToString(CultureInfo.InvariantCulture),
                set.Timeframe,
                values,
                numbers?.Trades.ToString(CultureInfo.InvariantCulture) ?? "-",
                Percent(numbers?.Mean),
                Number(numbers?.Amount),
                Percent(numbers?.WinShare),
                Percent(numbers?.Difference?.Mean),
                Number(numbers?.Difference?.T)));
        }

        await output.WriteLineAsync($"run page: /runs/{backtest.Id}");
        return 0;
    }

    private static string Sets(int count)
    {
        return count == 1 ? "1 set" : $"{count} sets";
    }

    private static string Percent(double? value)
    {
        return value is { } fraction ? (fraction * 100).ToString("0.###", CultureInfo.InvariantCulture) + " %" : "-";
    }

    private static string Number(double? value)
    {
        return value?.ToString("0.##", CultureInfo.InvariantCulture) ?? "-";
    }
}
