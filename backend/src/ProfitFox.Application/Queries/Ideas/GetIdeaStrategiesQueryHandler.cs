using MediatR;

using ProfitFox.Application.Commands.Strategies.Base;
using ProfitFox.Application.Contracts.Preview;
using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Queries.Filters;
using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Lists;
using ProfitFox.Domain.ValueObjects.Lists;
using ProfitFox.Domain.ValueObjects.Lists.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.Strategies.Enums;

namespace ProfitFox.Application.Queries.Ideas;

/// <summary>
///     The ideas are chosen as <see cref="GetIdeasQueryHandler" /> chooses those of the list, without the strategy, and
///     counted by the name of the strategy; a strategy of a category of ideas without such ideas gets a row of nought. The
///     name is kept in the idea, so the idea of a strategy that is gone counts by itself (design.md, decision 9). A strategy
///     saved before kinds had a column reads with the kind of its graph, as in the list of strategies.
/// </summary>
internal sealed class GetIdeaStrategiesQueryHandler(IIdeaRepository ideas, ISavedFilterRepository filters, IStrategyRepository strategies)
    : IRequestHandler<GetIdeaStrategiesQuery, IdeaStrategiesResult>
{
    private static readonly string[] States = ["open", "closed"];
    private static readonly string[] Sides = ["long", "short"];
    private static readonly string[] ExitReasons = ["stop", "target", "time"];

    public async Task<IdeaStrategiesResult> Handle(GetIdeaStrategiesQuery request, CancellationToken cancellationToken)
    {
        (InstrumentKind? pairKind, int? minScore) = Check(request);
        (IReadOnlyList<FilterCondition> conditions, bool gone) = await FilterResolver.ConditionsAsync(filters, request.Filter, FilterPlace.Ideas, cancellationToken);
        IdeaListKind kind = request.State == "closed" ? IdeaListKind.Closed : IdeaListKind.Open;
        ListOrder order = FilterResolver.Order(ListFields.Ideas, kind == IdeaListKind.Closed ? "exitTime" : "moment", new ListRequest(null, null, null, null, request.Filter));
        var query = new IdeaListQuery(
            kind,
            order,
            Search: request.Search,
            Timeframe: request.Timeframe,
            Side: request.Side,
            Label: request.Label,
            OnlyPositive: request.OnlyPositive,
            OnlyWithStop: request.OnlyWithStop,
            ExitReason: request.ExitReason,
            PeriodDays: request.PeriodDays,
            Conditions: conditions,
            PairKind: pairKind,
            MinScore: minScore);
        var counts = (await ideas.StrategyCountsAsync(query, cancellationToken)).ToDictionary(row => row.Strategy, row => row.Ideas, StringComparer.Ordinal);
        foreach (string name in await IdeaStrategiesAsync(cancellationToken))
        {
            counts.TryAdd(name, 0);
        }

        IdeaStrategyCount[] rows = [.. counts.OrderBy(row => row.Key, StringComparer.Ordinal).Select(row => new IdeaStrategyCount(row.Key, row.Value))];
        return new IdeaStrategiesResult(rows.Sum(row => row.Ideas), rows, gone);
    }

    // The choices of a list of values; a value outside the list is an error at its field. The kind of the pair asked for.
    private static (InstrumentKind? PairKind, int? MinScore) Check(GetIdeaStrategiesQuery request)
    {
        List<PreviewErrorInternal> errors = [];
        Choice(errors, "state", request.State, States);
        Choice(errors, "side", request.Side, Sides);
        Choice(errors, "exitReason", request.ExitReason, ExitReasons);
        if (request.Timeframe is { Length: > 0 } timeframe && !Timeframes.TryParse(timeframe, out _))
        {
            errors.Add(new PreviewErrorInternal("InvalidTimeframe", $"таймфрейм — один из {Timeframes.Listed}") { Field = "timeframe" });
        }

        if (FilterResolver.KindError(request.PairKind, out InstrumentKind? pairKind) is { } kindError)
        {
            errors.Add(kindError);
        }

        if (FilterResolver.MinScoreError(request.MinScore, out int? minScore) is { } scoreError)
        {
            errors.Add(scoreError);
        }

        if (request.PeriodDays is < 1)
        {
            errors.Add(new PreviewErrorInternal("InvalidPeriod", "период — число дней от 1") { Field = "period" });
        }

        if (errors.Count > 0)
        {
            throw new IdeasRequestException(errors);
        }

        return (pairKind, minScore);
    }

    private static void Choice(List<PreviewErrorInternal> errors, string field, string? value, string[] values)
    {
        if (value is { Length: > 0 } && !values.Contains(value, StringComparer.Ordinal))
        {
            errors.Add(new PreviewErrorInternal("InvalidChoice", $"{field} — одно из {string.Join(", ", values)}") { Field = field });
        }
    }

    private async Task<List<string>> IdeaStrategiesAsync(CancellationToken cancellationToken)
    {
        List<string> names = [];
        foreach (string name in await strategies.NamesAsync(cancellationToken))
        {
            try
            {
                StrategyDocument strategy = await StrategyChecks.ReadAsync(strategies, name, cancellationToken);
                if (strategy.Kind == StrategyKind.Idea)
                {
                    names.Add(strategy.Name);
                }
            }
            catch (StrategyLibraryException)
            {
                // A strategy that does not read gives no ideas and no row.
            }
        }

        return names;
    }
}
