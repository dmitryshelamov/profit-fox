using ProfitFox.Application.SavedFilters;

namespace ProfitFox.Presentation.Contracts.SavedFilters;

/// <summary>
///     A field of the catalog of saved filters: its code, the unit of its value (<c>usdt</c>, <c>percent</c>, <c>ratio</c>, <c>r</c> or
///     <c>choice</c>), whether it is a field of an idea, which acts only on «Идеи», the operators it takes and the codes a field of choices
///     takes.
/// </summary>
public sealed record SavedFilterFieldResponse(string Code, string Unit, bool OfIdea, IReadOnlyList<string> Operators, IReadOnlyList<string>? Choices)
{
    public static SavedFilterFieldResponse Of(SavedFilterField field)
    {
        return new SavedFilterFieldResponse(field.Code, field.Unit, field.OfIdea, [.. field.Operators.Select(op => op.ToCode())], field.Choices);
    }
}