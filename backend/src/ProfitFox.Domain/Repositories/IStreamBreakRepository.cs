using ProfitFox.Domain.ValueObjects.Market;

namespace ProfitFox.Domain.Repositories;

/// <summary>Stores the breaks of the streams whose missed events cannot be loaded again.</summary>
public interface IStreamBreakRepository
{
    Task AddAsync(StreamBreak streamBreak, CancellationToken cancellationToken);

    /// <summary>The breaks of <paramref name="stream" /> that overlap <c>[from, to)</c>.</summary>
    Task<IReadOnlyList<StreamBreak>> GetAsync(string stream, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}