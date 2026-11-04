namespace ProfitFox.Application.Ideas.Queries.GetPairIdeas;

/// <summary>The ideas of the pair, or validation errors keyed by parameter name.</summary>
public sealed record GetPairIdeasResult(IReadOnlyList<IdeaView>? Ideas, IReadOnlyDictionary<string, string> Errors);