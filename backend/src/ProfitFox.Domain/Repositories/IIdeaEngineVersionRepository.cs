namespace ProfitFox.Domain.Repositories;

/// <summary>Stores when the scanner first worked with a version of the rules of the ideas (decision 6 of honest-idea-intervals).</summary>
public interface IIdeaEngineVersionRepository
{
    /// <summary>Writes <paramref name="at" /> as the start of the version unless it has one; the first start stays.</summary>
    Task StartAsync(int engineVersion, DateTimeOffset at, CancellationToken cancellationToken);

    /// <summary>The start of the version; none before the scanner first took the lease with it.</summary>
    Task<DateTimeOffset?> GetStartAsync(int engineVersion, CancellationToken cancellationToken);
}