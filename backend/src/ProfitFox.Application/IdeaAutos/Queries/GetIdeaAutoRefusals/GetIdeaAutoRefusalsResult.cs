using ProfitFox.Application.Paging;

namespace ProfitFox.Application.IdeaAutos.Queries.GetIdeaAutoRefusals;

/// <summary>A page of the refusals, or, when a parameter is wrong, the errors by its name and no page.</summary>
public sealed record GetIdeaAutoRefusalsResult(Page<IdeaAutoRefusalView>? Refusals, IReadOnlyDictionary<string, string> Errors);