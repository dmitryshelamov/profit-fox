using ProfitFox.Domain.Entities.IdeaAutos;

namespace ProfitFox.Application.IdeaAutos;

/// <summary>
///     An auto of the list with the name of its account and its totals: the paper trades that closed, their mean result and their sum in R, the real
///     trades that closed and their sum in USDT after the fees, and the refusals since 00:00 UTC.
/// </summary>
public sealed record IdeaAutoSummary(
    IdeaAuto Auto,
    string AccountName,
    int PaperClosed,
    decimal? PaperMeanR,
    decimal PaperSumR,
    int RealClosed,
    decimal RealResult,
    int RefusalsToday);