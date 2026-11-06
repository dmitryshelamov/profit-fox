using System.ComponentModel.DataAnnotations;

using ProfitFox.Domain.ValueObjects.IdeaTrades;

namespace ProfitFox.Presentation.Contracts.IdeaTrades;

/// <summary>
///     The totals of the closed trades of a filter: those closed at the target, at the stop and manually, and apart from them the cancelled and
///     the failed. The money, the fees, the mean result in R with its 95 % interval (none for one trade, nor the mean for none) and the mean time
///     to the outcome are over the first three. Numbers are decimal strings.
/// </summary>
public sealed record IdeaJournalTotalsResponse(
    int Targets,
    int Stops,
    int Manual,
    int Cancelled,
    int Failed,
    [property: Required] string Result,
    [property: Required] string Fees,
    string? MeanR,
    string? MeanRLow,
    string? MeanRHigh,
    long? MeanSecondsToOutcome,
    PromiseComparisonResponse Promise,
    IdeaComparisonResponse Ideas)
{
    public static IdeaJournalTotalsResponse Of(IdeaJournalTotals totals)
    {
        return new IdeaJournalTotalsResponse(
            totals.Targets,
            totals.Stops,
            totals.Manual,
            totals.Cancelled,
            totals.Failed,
            IdeaTradeResponse.Text(totals.Result),
            IdeaTradeResponse.Text(totals.Fees),
            Text(totals.MeanR),
            Text(totals.MeanRLow),
            Text(totals.MeanRHigh),
            totals.MeanTime is { } time ? (long)time.TotalSeconds : null,
            new PromiseComparisonResponse(totals.Promise.Count, Text(totals.Promise.Promised), Text(totals.Promise.Got)),
            new IdeaComparisonResponse(totals.Idea.Count, Text(totals.Idea.Idea), Text(totals.Idea.Trades)));
    }

    private static string? Text(decimal? value)
    {
        return value is { } number ? IdeaTradeResponse.Text(number) : null;
    }
}

/// <summary>The trades with a promised expectancy: how many, the mean expectancy they were promised and the mean result in R they got.</summary>
public sealed record PromiseComparisonResponse(int Count, string? Promised, string? Got);

/// <summary>The trades whose idea has a result in R: how many, the mean result of their ideas and the mean result of the trades, in R.</summary>
public sealed record IdeaComparisonResponse(int Count, string? Ideas, string? Trades);