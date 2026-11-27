using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using MediatR;

using ProfitFox.Application.Commands.Signals.CreateSignal;
using ProfitFox.Application.Commands.Strategies.CreateStrategy;
using ProfitFox.Application.Contracts.Signals;
using ProfitFox.Application.Contracts.Strategies;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Execution;
using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.Services.Nodes;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Signals;
using ProfitFox.Domain.ValueObjects.Strategies.Enums;

namespace ProfitFox.Tests.Integration;

/// <summary>
///     The reference graphs of the setups of v2 (change add-setup-strategies, design.md, decision 10): the files of
///     <c>Fixtures/Setups</c> are the bodies of the requests that create the signals (<c>&lt;name&gt;.signal.json</c>) and the
///     strategies (<c>&lt;name&gt;.strategy.json</c>) on a bench. A strategy names each of its signals by the placeholder
///     <c>{{signal_&lt;name&gt;}}</c> — the name with <c>_</c> for <c>-</c> — that takes the id of the saved signal. The helper
///     saves them through the commands of the application and runs the saved strategy on candles a test prepares.
/// </summary>
internal static partial class SetupFixtures
{
    public static readonly string Folder = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Setups");

    /// <summary>
    ///     The names of the signals and of the strategies of the setups, in the order of their names.
    /// </summary>
    public static IReadOnlyList<string> Signals => Names(Folder, ".signal.json");

    public static IReadOnlyList<string> Strategies => Names(Folder, ".strategy.json");

    /// <summary>
    ///     The body of the request that creates the signal or the strategy of <paramref name="file" /> in <paramref name="folder" />.
    /// </summary>
    public static JsonObject Body(string file, string? folder = null)
    {
        return JsonNode.Parse(File.ReadAllText(Path.Combine(folder ?? Folder, file)))!.AsObject();
    }

    /// <summary>
    ///     The graph of the signal of the file, checked as a signal.
    /// </summary>
    public static Signal Signal(string name, string? folder = null)
    {
        ReadResult<Signal> read = StrategyGraph.ReadSignal(Body($"{name}.signal.json", folder)["graph"]!.ToJsonString());
        Assert.That(read.Errors, Is.Empty, $"the signal {name} passes the checks");
        return read.Value!;
    }

    /// <summary>
    ///     The names of the signals the strategy of the file points at, in the order of its nodes.
    /// </summary>
    public static IReadOnlyList<string> SignalsOf(JsonObject strategy)
    {
        return [.. Placeholder().Matches(strategy["graph"]!.ToJsonString()).Select(match => match.Groups[1].Value.Replace('_', '-')).Distinct()];
    }

    /// <summary>
    ///     Saves the strategy of the file, under <paramref name="name" /> when given, and the signals it points at, those not
    ///     saved yet, by the commands of the application; <paramref name="change" /> may change the body of the strategy first.
    /// </summary>
    public static async Task<SavedSetup> SaveAsync(
        ISender sender,
        ISignalRepository signals,
        string strategy,
        string? folder = null,
        string? name = null,
        Action<JsonObject>? change = null)
    {
        JsonObject body = Body($"{strategy}.strategy.json", folder);
        change?.Invoke(body);
        string graph = body["graph"]!.ToJsonString();
        foreach (string signal in SignalsOf(body))
        {
            Guid id = await (await signals.ExistsAsync(signal, CancellationToken.None)
                ? Existing(signals, signal)
                : Create(sender, signal, folder));
            graph = graph.Replace("{{signal_" + signal.Replace('-', '_') + "}}", id.ToString(), StringComparison.Ordinal);
        }

        using var document = JsonDocument.Parse(graph);
        StrategyInternal saved = await sender.Send(
            new CreateStrategyCommand(
                name ?? (string)body["name"]!,
                (string?)body["description"],
                document.RootElement.Clone(),
                new Dictionary<string, NodePosition>(),
                StrategyKind.Idea));
        SignalReference[] references =
        [
            .. saved.Graph.Nodes
                .Where(node => node.Kind == NodeCatalog.Signal)
                .Select(node => new SignalReference(Guid.Parse(node.Settings[NodeCatalog.SignalSetting]), int.Parse(node.Settings[NodeCatalog.VersionSetting], System.Globalization.CultureInfo.InvariantCulture)))
        ];
        var set = new SignalSet(await signals.ReadVersionsAsync(references, CancellationToken.None));
        return new SavedSetup(saved, set);
    }

    private static async Task<Guid> Create(ISender sender, string signal, string? folder)
    {
        JsonObject body = Body($"{signal}.signal.json", folder);
        using var graph = JsonDocument.Parse(body["graph"]!.ToJsonString());
        SignalInternal saved = await sender.Send(
            new CreateSignalCommand((string)body["name"]!, (string?)body["description"], graph.RootElement.Clone(), new Dictionary<string, NodePosition>()));
        return saved.Id;
    }

    private static async Task<Guid> Existing(ISignalRepository signals, string signal)
    {
        return (await signals.ReadAsync(signal, CancellationToken.None))!.Signal!.Id;
    }

    private static List<string> Names(string folder, string suffix)
    {
        return [.. Directory.GetFiles(folder, $"*{suffix}").Select(path => Path.GetFileName(path)[..^suffix.Length]).Order(StringComparer.Ordinal)];
    }

    [GeneratedRegex(@"\{\{signal_([a-z0-9_]+)\}\}")]
    private static partial Regex Placeholder();
}

/// <summary>
///     A strategy of the setups as the bench saved it, with the versions of the signals it points at.
/// </summary>
internal sealed record SavedSetup(StrategyInternal Strategy, SignalSet Signals)
{
    /// <summary>
    ///     The strategy as a backtest and the scanner read it: its graph with the versions of its signals.
    /// </summary>
    public TradingStrategy Trading
    {
        get
        {
            using var document = JsonDocument.Parse(StrategyGraph.Write(Strategy.Graph));
            ReadResult<TradingStrategy> read = StrategyGraph.ReadStrategy(document.RootElement, Signals, StrategyKind.Idea);
            Assert.That(read.Errors, Is.Empty, $"the saved strategy {Strategy.Name} passes the checks");
            return read.Value!;
        }
    }

    /// <summary>
    ///     The graph with its signal nodes replaced by the graphs of their versions, as a backtest gets it.
    /// </summary>
    public GraphDefinition Expanded => Trading.Graph;

    /// <summary>
    ///     The step of the strategy at every candle of one pair, the last payment by each close from <paramref name="funding" />.
    /// </summary>
    public List<StrategyStep> Steps(IEnumerable<ClosedCandle> candles, Func<ClosedCandle, FundingPayment?>? funding = null)
    {
        var executor = new StrategyExecutor(Trading);
        return [.. candles.Select(candle => executor.Step("BTCUSDT", candle, funding?.Invoke(candle)))];
    }

    /// <summary>
    ///     The steps that carry an entry order.
    /// </summary>
    public List<StrategyStep> Orders(IEnumerable<ClosedCandle> candles, Func<ClosedCandle, FundingPayment?>? funding = null)
    {
        return [.. Steps(candles, funding).Where(step => step.Entry is not null)];
    }
}
