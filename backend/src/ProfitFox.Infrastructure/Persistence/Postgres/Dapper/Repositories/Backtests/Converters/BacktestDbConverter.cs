using System.Globalization;
using System.Text.Json;

using ProfitFox.Domain.Entities.Backtests;
using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Hypotheses;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Domain.ValueObjects.Strategies.Enums;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Backtests.Contracts;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Backtests.Converters;

/// <summary>
///     A backtest and its rows. The graph is kept as the engine writes it; the settings, the values of a set and the numbers
///     as JSON objects of their own form, so the domain can rename a field without breaking what is stored.
/// </summary>
internal static class BacktestDbConverter
{
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    public static string WriteGraph(Backtest backtest)
    {
        return StrategyGraph.Write(backtest.Graph);
    }

    public static string WriteSettings(BacktestSettings settings)
    {
        var stored = new SettingsJson(
            [.. settings.Values.Select(parameter => new ParameterJson(parameter.Name, parameter.Values))],
            [.. settings.Timeframes.Select(Timeframes.Name)],
            Date(settings.Start),
            Date(settings.End),
            settings.Pairs,
            settings.TakerFee,
            settings.MakerFee);
        return JsonSerializer.Serialize(stored, Json);
    }

    public static string WriteValues(BacktestSet set)
    {
        return JsonSerializer.Serialize(set.Values.Select(value => new ValueJson(value.Key, value.Value)), Json);
    }

    public static string WriteNumbers(SetSummary numbers)
    {
        var stored = new NumbersJson(
            numbers.Orders,
            numbers.Trades,
            numbers.PairsWithTrades,
            numbers.Mean,
            numbers.Amount,
            numbers.WinShare,
            new ExclusionsJson(numbers.Excluded.Plan, numbers.Excluded.Coverage, numbers.Excluded.Funding, numbers.Excluded.NoTrade, numbers.Excluded.Period),
            numbers.Skipped,
            numbers.OpenCycles,
            numbers.OpenAmount,
            numbers.Difference is { } difference ? new DifferenceJson(difference.Mean, difference.Weeks, difference.T) : null);
        return JsonSerializer.Serialize(stored, Json);
    }

    public static SetSummary ReadNumbers(string text)
    {
        NumbersJson stored = JsonSerializer.Deserialize<NumbersJson>(text, Json)!;
        return new SetSummary(
            stored.Orders,
            stored.Trades,
            stored.PairsWithTrades,
            stored.Mean,
            stored.Amount,
            stored.WinShare,
            new Exclusions(stored.Excluded.Plan, stored.Excluded.Coverage, stored.Excluded.Funding, stored.Excluded.NoTrade, stored.Excluded.Period),
            stored.Skipped,
            stored.OpenCycles,
            stored.OpenAmount,
            stored.Difference is { } difference ? new RandomDifference(difference.Mean, difference.Weeks, difference.T) : null);
    }

    /// <summary>
    ///     The backtest of a row, with the sets given.
    /// </summary>
    public static Backtest ToDomain(BacktestDb row, IReadOnlyList<BacktestSetDb>? sets = null)
    {
        using var graph = JsonDocument.Parse(row.Graph);
        StrategyKinds.TryParse(row.Kind, out StrategyKind kind);
        return new Backtest
        {
            Id = row.Id,
            StrategyId = row.StrategyId,
            StrategyName = row.StrategyName,
            StrategyVersion = row.StrategyVersion,
            Kind = kind,
            Graph = StrategyGraph.ReadForm(graph.RootElement).Value ?? throw new InvalidOperationException($"The graph of backtest {row.Id} does not read."),
            Settings = ReadSettings(row.Settings),
            SetCount = row.Sets,
            Sets = sets is null ? [] : [.. sets.OrderBy(set => set.Number).Select(ToDomain)],
            Source = row.SourceId is { } source ? new BacktestSource(source, row.SourceSet!.Value) : null,
            Status = row.Status,
            QueuedAt = row.QueuedAt,
            StartedAt = row.StartedAt,
            FinishedAt = row.FinishedAt,
            LeaseOwner = row.LeaseOwner,
            LeasedUntil = row.LeasedUntil,
            LostLeases = row.LostLeases,
            CancelRequested = row.CancelRequested,
            PairsDone = row.PairsDone,
            PairsTotal = row.PairsTotal,
            EngineVersion = row.EngineVersion,
            DataHash = row.DataHash,
            ReadSeconds = row.ReadSeconds,
            ComputeSeconds = row.ComputeSeconds,
            Reason = row.Reason,
            BestAmount = row.BestAmount
        };
    }

    public static PairSummary ToDomain(BacktestPairDb row)
    {
        return new PairSummary(row.Pair, ReadNumbers(row.Numbers));
    }

    private static BacktestSetOutcome ToDomain(BacktestSetDb row)
    {
        Timeframes.TryParse(row.Timeframe, out TimeSpan timeframe);
        ValueJson[] values = JsonSerializer.Deserialize<ValueJson[]>(row.Values, Json)!;
        var set = new BacktestSet(row.Number, timeframe, [.. values.Select(value => new KeyValuePair<string, string>(value.Name, value.Value))]);
        return new BacktestSetOutcome(set, row.Numbers is { } numbers ? ReadNumbers(numbers) : null);
    }

    private static BacktestSettings ReadSettings(string text)
    {
        SettingsJson stored = JsonSerializer.Deserialize<SettingsJson>(text, Json)!;
        return new BacktestSettings(
            [.. stored.Values.Select(parameter => new ParameterValues(parameter.Name, parameter.Values))],
            [.. stored.Timeframes.Select(name => Timeframes.TryParse(name, out TimeSpan timeframe) ? timeframe : throw new InvalidOperationException($"{name} is not a timeframe."))],
            ReadDate(stored.Start),
            ReadDate(stored.End),
            stored.Pairs,
            stored.TakerFee,
            stored.MakerFee);
    }

    private static string Date(DateTimeOffset moment)
    {
        return moment.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset ReadDate(string text)
    {
        return DateTimeOffset.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    }

    private sealed record SettingsJson(
        IReadOnlyList<ParameterJson> Values,
        IReadOnlyList<string> Timeframes,
        string Start,
        string End,
        IReadOnlyList<string>? Pairs,
        double TakerFee,
        double MakerFee);

    private sealed record ParameterJson(string Name, IReadOnlyList<string> Values);

    private sealed record ValueJson(string Name, string Value);

    private sealed record NumbersJson(
        int Orders,
        int Trades,
        int PairsWithTrades,
        double? Mean,
        double Amount,
        double? WinShare,
        ExclusionsJson Excluded,
        int Skipped,
        int OpenCycles,
        double OpenAmount,
        DifferenceJson? Difference);

    private sealed record ExclusionsJson(int Plan, int Coverage, int Funding, int NoTrade, int Period);

    private sealed record DifferenceJson(double Mean, int Weeks, double? T);
}
