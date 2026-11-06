using System.ComponentModel.DataAnnotations;

using ProfitFox.Application.IdeaAutos;

namespace ProfitFox.Presentation.Contracts.IdeaAutos;

/// <summary>
///     An auto of the list with its totals: the paper trades that closed (their number, their mean result and their sum in R), the real trades that closed
///     (their number and their sum in USDT after the fees) and the refusals since 00:00 UTC. Numbers are decimal strings.
/// </summary>
public sealed record IdeaAutoItemResponse(
    [property: Required] IdeaAutoResponse Auto,
    int PaperClosed,
    string? PaperMeanR,
    [property: Required] string PaperSumR,
    int RealClosed,
    [property: Required] string RealResult,
    int RefusalsToday)
{
    public static IdeaAutoItemResponse Of(IdeaAutoSummary summary)
    {
        return new IdeaAutoItemResponse(
            IdeaAutoResponse.Of(summary),
            summary.PaperClosed,
            summary.PaperMeanR is { } mean ? IdeaAutoResponse.Text(mean) : null,
            IdeaAutoResponse.Text(summary.PaperSumR),
            summary.RealClosed,
            IdeaAutoResponse.Text(summary.RealResult),
            summary.RefusalsToday);
    }
}