using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Application.Scanner.Ideas;

/// <summary>
///     The calibration of the buckets (decision 6): the cases of the version fired in the 90 days checked and the 90 days of history before
///     them, read once and walked in memory by <see cref="CalibrationCalculator" />; the reports replace the stored ones whole.
/// </summary>
public sealed class IdeaCalibration(IIdeaCaseRepository cases, IIdeaCalibrationRepository calibrations, IdeasEngineVersion version)
{
    private static readonly TimeSpan Read = TimeSpan.FromDays(180);

    public async Task RecomputeAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        IReadOnlyList<IdeaOutcome> outcomes = await cases.GetOutcomesAsync(version.Number, now - Read, cancellationToken);
        await calibrations.ReplaceAsync(version.Number, CalibrationCalculator.Compute(outcomes, now), cancellationToken);
    }
}