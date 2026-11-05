using ProfitFox.Application.SavedFilters.Queries.FitSavedFilter;

namespace ProfitFox.Presentation.Contracts.SavedFilters;

/// <summary>How many pairs of the snapshot and how many ideas of the list meet the conditions of a draft.</summary>
public sealed record SavedFilterFitResponse(int Pairs, int Ideas)
{
    public static SavedFilterFitResponse Of(FitSavedFilterResult result)
    {
        return new SavedFilterFitResponse(result.Pairs, result.Ideas);
    }
}