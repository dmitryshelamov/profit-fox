using ProfitFox.Application.Ideas.Queries.GetIdeas;

namespace ProfitFox.Presentation.Contracts.Ideas;

/// <summary>A page of ideas: its number from 1 (the last page when the one asked for is past the end), its size and the count of all the ideas that meet the filters.</summary>
public sealed record IdeasPageResponse(IReadOnlyList<IdeaResponse> Items, int Page, int PageSize, int Total)
{
    public static IdeasPageResponse Of(GetIdeasResult result)
    {
        return new IdeasPageResponse([.. result.Page!.Items.Select(IdeaResponse.Of)], result.Page.Number, result.Page.Size, result.Page.Total);
    }
}