using ProfitFox.Application.Paging;

namespace ProfitFox.Application.Ideas.Queries.GetIdeas;

/// <summary>The page of ideas, or validation errors keyed by parameter name.</summary>
public sealed record GetIdeasResult(Page<IdeaView>? Page, IReadOnlyDictionary<string, string> Errors);