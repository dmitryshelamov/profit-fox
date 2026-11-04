using ProfitFox.Domain.Entities.SavedFilters.Enums;

namespace ProfitFox.Application.SavedFilters;

/// <summary>
///     A field of the catalog of saved filters: its code, the unit of its value (<c>usdt</c>, <c>percent</c>, <c>ratio</c>, <c>r</c>,
///     <c>count</c> or <c>flag</c>), the operators it takes and whether it is a field of an idea, which acts only on «Идеи».
/// </summary>
public sealed record SavedFilterField(FilterField Field, string Code, string Unit, bool OfIdea, IReadOnlyList<FilterOperator> Operators);