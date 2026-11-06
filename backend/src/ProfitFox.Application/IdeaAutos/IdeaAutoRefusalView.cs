using ProfitFox.Domain.Entities.IdeaAutos;

namespace ProfitFox.Application.IdeaAutos;

/// <summary>A refusal with the name of its auto, the auto of a deleted one too.</summary>
public sealed record IdeaAutoRefusalView(IdeaAutoRefusal Refusal, string AutoName);