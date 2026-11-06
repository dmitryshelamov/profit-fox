using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Domain.Repositories;

/// <summary>Stores the reports of the calibration, which the scanner computes with the buckets and the API reads.</summary>
public interface IIdeaCalibrationRepository
{
    /// <summary>Replaces the reports of the version with <paramref name="reports" /> and removes the ones of other versions.</summary>
    Task ReplaceAsync(int engineVersion, IReadOnlyCollection<CalibrationReport> reports, CancellationToken cancellationToken);

    /// <summary>The report of a window of days and a category, all of them without one; none before the first calibration.</summary>
    Task<CalibrationReport?> GetAsync(int engineVersion, int days, MarketCategory? category, CancellationToken cancellationToken);
}