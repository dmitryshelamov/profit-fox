using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Application.Scanner.Ideas;

/// <summary>
///     What one pass over the signals gives: the buckets of 90 and 30 days, how each counted case ended, for the calibration, and the roots of
///     the categories the buckets were shrunk to, by the days of the window.
/// </summary>
public sealed record IdeaOutcomesResult(
    IReadOnlyList<IdeaBucket> Buckets,
    IReadOnlyList<IdeaOutcome> Outcomes,
    IReadOnlyDictionary<int, IReadOnlyDictionary<MarketCategory, BucketModel.Root>> Roots);