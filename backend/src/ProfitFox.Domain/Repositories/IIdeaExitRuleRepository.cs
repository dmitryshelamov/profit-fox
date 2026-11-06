using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Domain.Repositories;

/// <summary>Stores the exit rules applied (decision 5); a type × interval without one has the default.</summary>
public interface IIdeaExitRuleRepository
{
    Task<IReadOnlyList<IdeaExitRule>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>Writes the rule of its type × interval over the one applied before.</summary>
    Task SaveAsync(IdeaExitRule rule, CancellationToken cancellationToken);
}