using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Domain.Repositories;

/// <summary>
///     The enabled strategies and labels with their latest version numbers in one answer (change add-ideas-and-market, task
///     18.13): the scanner looks at them every few seconds and reads a graph only when its version changed.
/// </summary>
public interface IIdeaSourceRepository
{
    Task<IdeaSources> SourcesAsync(CancellationToken cancellationToken);
}
