using System.Text.Json;

using MediatR;

using ProfitFox.Application.Commands.Strategies.Base;
using ProfitFox.Application.Contracts.Preview;
using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Strategies.Enums;

namespace ProfitFox.Application.Commands.Strategies.SetStrategyIdeas;

/// <summary>
///     Only a category of ideas gives ideas; turned on it needs a timeframe, a latest version that passes the checks with the
///     signals it points at, and named pairs that are listed. Every error comes at once; turned off, only the timeframes are
///     checked.
/// </summary>
internal sealed class SetStrategyIdeasCommandHandler(
    IStrategyRepository strategies,
    ISignalRepository signals,
    IStrategyIdeasRepository ideas,
    IInstrumentCatalog catalog) : IRequestHandler<SetStrategyIdeasCommand, StrategyIdeas>
{
    /// <exception cref="Domain.Exceptions.StrategyLibraryException">There is no such strategy, or it does not read.</exception>
    /// <exception cref="IdeasRequestException">The request has an error.</exception>
    public async Task<StrategyIdeas> Handle(SetStrategyIdeasCommand request, CancellationToken cancellationToken)
    {
        StrategyDocument strategy = await StrategyChecks.ReadAsync(strategies, request.Strategy, cancellationToken);
        List<PreviewErrorInternal> errors = [];
        if (strategy.Kind != StrategyKind.Idea)
        {
            throw new IdeasRequestException([new PreviewErrorInternal("BotGivesNoIdeas", "стратегия для бота идей не даёт") { Field = "strategy" }]);
        }

        List<TimeSpan> timeframes = [];
        foreach (string name in request.Timeframes ?? [])
        {
            if (!Timeframes.TryParse(name, out TimeSpan timeframe))
            {
                errors.Add(new PreviewErrorInternal("InvalidTimeframe", $"таймфрейм {name} не из {Timeframes.Listed}") { Field = "timeframes" });
            }
            else if (!timeframes.Contains(timeframe))
            {
                timeframes.Add(timeframe);
            }
        }

        if (request.Enabled && timeframes.Count == 0 && errors.Count == 0)
        {
            errors.Add(new PreviewErrorInternal("NoTimeframes", "идеи включаются хотя бы на одном таймфрейме") { Field = "timeframes" });
        }

        if (request.Enabled)
        {
            using var graph = JsonDocument.Parse(StrategyGraph.Write(strategy.Graph));
            StrategyGraphCheck check = await StrategyChecks.CheckAsync(signals, graph.RootElement, StrategyKind.Idea, cancellationToken);
            errors.AddRange(check.Errors.Select(PreviewErrorInternal.Of));
            if (check.Strategy is { } checkedStrategy)
            {
                string[] listed = [.. (await catalog.LinearUsdtPerpetuals(cancellationToken)).Select(pair => pair.Symbol)];
                errors.AddRange(StrategyGraph.CheckNamedPairs(checkedStrategy.Graph, listed).Select(PreviewErrorInternal.Of));
            }
        }

        if (errors.Count > 0)
        {
            throw new IdeasRequestException(errors);
        }

        var result = new StrategyIdeas(strategy.Id, request.Enabled, [.. timeframes.Order()]);
        await ideas.SetAsync(result, cancellationToken);
        return result;
    }
}
