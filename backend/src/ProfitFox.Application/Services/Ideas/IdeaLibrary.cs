using System.Text.Json;

using ProfitFox.Application.Commands.Strategies.Base;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.Strategies.Enums;

namespace ProfitFox.Application.Services.Ideas;

/// <summary>
///     What the scanner runs (change add-ideas-and-market, design.md, decisions 6 and 7): every enabled strategy of the kind
///     «категория идей» on each of its timeframes, by its latest version checked with the signals it points at, and every
///     enabled label by the latest version of its signal. A strategy or a signal that does not read now is left out.
/// </summary>
public sealed class IdeaLibrary(
    IStrategyRepository strategies,
    ISignalRepository signals,
    IStrategyIdeasRepository ideas,
    ISignalLabelRepository labels)
{
    public async Task<IReadOnlyList<StrategyRunSpec>> StrategiesAsync(CancellationToken cancellationToken)
    {
        var enabled = (await ideas.AllAsync(cancellationToken)).Where(item => item.Enabled && item.Timeframes.Count > 0).ToDictionary(item => item.StrategyId);
        if (enabled.Count == 0)
        {
            return [];
        }

        List<StrategyRunSpec> runs = [];
        foreach (string name in (await strategies.NamesAsync(cancellationToken)).Order(StringComparer.Ordinal))
        {
            if (await strategies.ReadAsync(name, cancellationToken) is not { Strategy: { } strategy } || !enabled.TryGetValue(strategy.Id, out StrategyIdeas? on)
                || strategy.Kind != StrategyKind.Idea)
            {
                continue;
            }

            using var graph = JsonDocument.Parse(StrategyGraph.Write(strategy.Graph));
            StrategyGraphCheck check = await StrategyChecks.CheckAsync(signals, graph.RootElement, StrategyKind.Idea, cancellationToken);
            if (check.Strategy is not { } checkedStrategy)
            {
                continue;
            }

            runs.AddRange(on.Timeframes.Distinct().Order().Select(timeframe => new StrategyRunSpec(strategy.Id, strategy.Name, strategy.Version, timeframe, checkedStrategy)));
        }

        return runs;
    }

    public async Task<IReadOnlyList<LabelRunSpec>> LabelsAsync(CancellationToken cancellationToken)
    {
        var enabled = (await labels.AllAsync(cancellationToken)).Where(label => label.Enabled).ToDictionary(label => label.SignalId);
        if (enabled.Count == 0)
        {
            return [];
        }

        List<LabelRunSpec> runs = [];
        foreach (string name in (await signals.NamesAsync(cancellationToken)).Order(StringComparer.Ordinal))
        {
            if (await signals.ReadAsync(name, cancellationToken) is not { Signal: { } document } || !enabled.TryGetValue(document.Id, out SignalLabel? label))
            {
                continue;
            }

            if (StrategyGraph.ReadSignal(StrategyGraph.Write(document.Graph)).Value is { } signal)
            {
                runs.Add(new LabelRunSpec(document.Id, label.Text, document.Version, label.Timeframe, signal));
            }
        }

        return runs;
    }
}
