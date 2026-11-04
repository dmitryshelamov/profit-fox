using System.Globalization;

using ProfitFox.Application.SavedFilters;
using ProfitFox.Domain.Entities.SavedFilters;

namespace ProfitFox.Presentation.Contracts.SavedFilters;

public sealed record SavedFilterResponse(Guid Id, string Name, string Place, IReadOnlyList<SavedFilterConditionBody> Conditions)
{
    public static SavedFilterResponse Of(SavedFilter filter)
    {
        return new SavedFilterResponse(
            filter.Id,
            filter.Name,
            filter.Place.ToCode(),
            [
                .. filter.Conditions.Select(condition => new SavedFilterConditionBody(
                    SavedFilterFields.Of(condition.Field).Code,
                    condition.Operator.ToCode(),
                    condition.Value?.ToString(CultureInfo.InvariantCulture),
                    condition.To?.ToString(CultureInfo.InvariantCulture)))
            ]);
    }
}