using System.Text.Json;

using ProfitFox.Application.Commands.Strategies.Base;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.Strategies.Enums;

namespace ProfitFox.Application.Services.Ideas;

/// <summary>
///     What the scanner runs (change add-ideas-and-market, design.md, decisions 6 and 7): every enabled strategy of the kind
///     «категория идей» on each of its timeframes, by its latest version checked with the signals it points at, and every
///     enabled label by the latest version of its signal. A strategy or a signal that does not read now is left out. The
///     enabled ones and their version numbers come in one answer; a graph is read and checked only when its version is new
///     to this library (task 18.13).
/// </summary>
public sealed class IdeaLibrary(IStrategyRepository strategies, ISignalRepository signals, IIdeaSourceRepository sources)
{
    private readonly Dictionary<Guid, (int Version, TradingStrategy? Strategy)> _strategies = [];
    private readonly Dictionary<Guid, (int Version, Signal? Signal)> _signals = [];

    /// <summary>
    ///     The enabled strategies on their timeframes and the enabled labels as they are now.
    /// </summary>
    public async Task<(IReadOnlyList<StrategyRunSpec> Strategies, IReadOnlyList<LabelRunSpec> Labels)> CurrentAsync(CancellationToken cancellationToken)
    {
        IdeaSources now = await sources.SourcesAsync(cancellationToken);
        List<StrategyRunSpec> runs = [];
        foreach (StrategySource source in now.Strategies.OrderBy(source => source.Name, StringComparer.Ordinal))
        {
            if (!_strategies.TryGetValue(source.StrategyId, out (int Version, TradingStrategy? Strategy) held) || held.Version != source.Version)
            {
                held = (source.Version, await ReadStrategyAsync(source, cancellationToken));
                _strategies[source.StrategyId] = held;
            }

            if (held.Strategy is { } strategy)
            {
                runs.AddRange(source.Timeframes.Distinct().Order().Select(timeframe => new StrategyRunSpec(source.StrategyId, source.Name, source.Version, timeframe, strategy)));
            }
        }

        List<LabelRunSpec> labels = [];
        foreach (LabelSource source in now.Labels.OrderBy(source => source.Name, StringComparer.Ordinal))
        {
            if (!_signals.TryGetValue(source.SignalId, out (int Version, Signal? Signal) held) || held.Version != source.Version)
            {
                held = (source.Version, await ReadSignalAsync(source, cancellationToken));
                _signals[source.SignalId] = held;
            }

            if (held.Signal is { } signal)
            {
                labels.Add(new LabelRunSpec(source.SignalId, source.Text, source.Version, source.Timeframe, signal));
            }
        }

        return (runs, labels);
    }

    // The latest version of the strategy checked with the signals it points at; null when it is not one to run.
    private async Task<TradingStrategy?> ReadStrategyAsync(StrategySource source, CancellationToken cancellationToken)
    {
        if (await strategies.ReadAsync(source.Name, cancellationToken) is not { Strategy: { } strategy } || strategy.Id != source.StrategyId
            || strategy.Kind != StrategyKind.Idea)
        {
            return null;
        }

        using var graph = JsonDocument.Parse(StrategyGraph.Write(strategy.Graph));
        StrategyGraphCheck check = await StrategyChecks.CheckAsync(signals, graph.RootElement, StrategyKind.Idea, cancellationToken);
        return check.Strategy;
    }

    private async Task<Signal?> ReadSignalAsync(LabelSource source, CancellationToken cancellationToken)
    {
        if (await signals.ReadAsync(source.Name, cancellationToken) is not { Signal: { } document } || document.Id != source.SignalId)
        {
            return null;
        }

        return StrategyGraph.ReadSignal(StrategyGraph.Write(document.Graph)).Value;
    }
}
