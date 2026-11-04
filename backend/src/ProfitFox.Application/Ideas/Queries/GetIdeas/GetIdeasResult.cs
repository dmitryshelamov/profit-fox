namespace ProfitFox.Application.Ideas.Queries.GetIdeas;

/// <summary>The ideas, or validation errors keyed by parameter name.</summary>
public sealed record GetIdeasResult(IReadOnlyList<IdeaView>? Ideas, IReadOnlyDictionary<string, string> Errors);