using ProfitFox.Domain.ValueObjects.Lists;
using ProfitFox.Domain.ValueObjects.Lists.Enums;

namespace ProfitFox.Domain.Entities.Filters;

/// <summary>
///     A saved filter (change add-ideas-and-market, spec saved-filters «Сохранённый фильтр»): its name, one of its kind, where
///     it is shown and its conditions, joined by «и».
/// </summary>
public sealed record SavedFilter(Guid Id, string Name, FilterPlace Place, IReadOnlyList<FilterCondition> Conditions)
{
    /// <summary>
    ///     Whether the filter is shown in <paramref name="place" />, «Рынок» or «Идеи».
    /// </summary>
    public bool ShownIn(FilterPlace place)
    {
        return Place == FilterPlace.Both || Place == place;
    }
}
