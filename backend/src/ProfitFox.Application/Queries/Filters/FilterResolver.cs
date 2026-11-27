using ProfitFox.Application.Contracts.Preview;
using ProfitFox.Application.Exceptions;
using ProfitFox.Domain.Entities.Filters;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Lists;
using ProfitFox.Domain.ValueObjects.Lists;
using ProfitFox.Domain.ValueObjects.Lists.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.MarketData.Enums;

namespace ProfitFox.Application.Queries.Filters;

/// <summary>
///     The page and the saved filter of a request of a list (change add-ideas-and-market, design.md, decisions 10 and 11): a
///     field of sorting outside the list, a wrong page or size, or a filter of the other place refuses the request. A filter
///     that is not there any more — deleted in another tab — is no filter, and the list says it went (spec saved-filters
///     «Фильтр удалили в другой вкладке»).
/// </summary>
internal static class FilterResolver
{
    public static ListOrder Order(IReadOnlyList<ListField> fields, string defaultSort, ListRequest request)
    {
        List<FilterProblem> problems = [];
        ListOrder? order = ListFields.Order(fields, defaultSort, request.Sort, request.Direction, request.Page, request.PageSize, problems);
        return order ?? throw Refused(problems);
    }

    /// <summary>
    ///     The conditions of the filter, and whether the filter asked for is gone.
    /// </summary>
    public static async Task<(IReadOnlyList<FilterCondition> Conditions, bool Gone)> ConditionsAsync(
        ISavedFilterRepository filters,
        Guid? id,
        FilterPlace place,
        CancellationToken cancellationToken)
    {
        if (id is not { } filterId)
        {
            return ([], false);
        }

        if (await filters.FindAsync(filterId, cancellationToken) is not { } filter)
        {
            return ([], true);
        }

        if (!filter.ShownIn(place))
        {
            throw Refused([new FilterProblem("FilterOfAnotherPlace", $"фильтр «{filter.Name}» не для этого раздела") { Field = "filter" }]);
        }

        return (filter.Conditions, false);
    }

    /// <summary>
    ///     The kind of a pair a list is asked for (change add-instrument-kind-filter, decision 4): <c>crypto</c>, <c>stock</c>,
    ///     or every kind when not given; another value refuses the request at the field <c>kind</c>, as a side does.
    /// </summary>
    public static InstrumentKind? Kind(string? name)
    {
        return KindError(name, out InstrumentKind? kind) is { } error ? throw new IdeasRequestException([error]) : kind;
    }

    /// <summary>
    ///     The error of the kind <paramref name="name" />, null for a kind or none, for a request that gathers its errors.
    /// </summary>
    public static PreviewErrorInternal? KindError(string? name, out InstrumentKind? kind)
    {
        kind = null;
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        if (InstrumentKinds.TryParse(name, out InstrumentKind known))
        {
            kind = known;
            return null;
        }

        return new PreviewErrorInternal("InvalidChoice", $"kind — одно из {InstrumentKinds.Listed}") { Field = "kind" };
    }

    /// <summary>
    ///     The least score a list is asked for (change add-idea-score, design.md, decision 3): a whole number, or none when not
    ///     given; another value refuses the request at the field <c>minScore</c>.
    /// </summary>
    public static int? MinScore(string? text)
    {
        return MinScoreError(text, out int? score) is { } error ? throw new IdeasRequestException([error]) : score;
    }

    /// <summary>
    ///     The error of the least score <paramref name="text" />, null for a whole number or none.
    /// </summary>
    public static PreviewErrorInternal? MinScoreError(string? text, out int? score)
    {
        score = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (int.TryParse(text.Trim(), System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out int value))
        {
            score = value;
            return null;
        }

        return new PreviewErrorInternal("InvalidMinScore", "балл от — целое число") { Field = "minScore" };
    }

    public static IdeasRequestException Refused(IEnumerable<FilterProblem> problems)
    {
        return new IdeasRequestException([.. problems.Select(Error)]);
    }

    public static PreviewErrorInternal Error(FilterProblem problem)
    {
        return new PreviewErrorInternal(problem.Code, problem.Reason) { Field = problem.Field, Condition = problem.Condition };
    }

    /// <summary>
    ///     Whether a filter is of the place, for a filter of the list.
    /// </summary>
    public static bool Of(SavedFilter filter, FilterPlace place)
    {
        return filter.ShownIn(place);
    }
}
