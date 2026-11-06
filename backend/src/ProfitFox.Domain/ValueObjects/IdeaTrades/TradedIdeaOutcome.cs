using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Domain.ValueObjects.IdeaTrades;

/// <summary>
///     How the idea of a trade ended, kept on the trade so that the clean-up of closed ideas does not take it away: the status the idea closed
///     with, when, and its result in R after the fees of its category (none for an idea that never entered).
/// </summary>
public sealed record TradedIdeaOutcome(IdeaStatus Status, DateTimeOffset StatusAt, decimal? Result)
{
    public static TradedIdeaOutcome Of(IdeaTrack closed, FeeRates fees)
    {
        return new TradedIdeaOutcome(closed.Status, closed.StatusAt, CaseResult.R(closed, fees));
    }
}