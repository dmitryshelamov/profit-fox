using ProfitFox.Domain.Entities.SavedFilters.Enums;

namespace ProfitFox.Application.SavedFilters;

/// <summary>
///     A field of the catalog of saved filters: its code, the unit of its value (<c>usdt</c>, <c>percent</c>, <c>ratio</c>, <c>r</c> or
///     <c>choice</c>), the operators it takes, whether it is a field of an idea, which acts only on «Идеи», and for a field of choices the codes
///     it takes.
/// </summary>
public sealed record SavedFilterField(FilterField Field, string Code, string Unit, bool OfIdea, IReadOnlyList<FilterOperator> Operators, IReadOnlyList<string>? Choices = null);