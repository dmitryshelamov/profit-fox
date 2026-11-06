using ProfitFox.Application.IdeaTrades.Queries.GetIdeaJournal;

namespace ProfitFox.Presentation.Contracts.IdeaTrades;

/// <summary>The journal: the open trades, the newest first, a page of the closed ones by the time they closed, the totals of all the closed real ones of the filter and those of the paper ones.</summary>
public sealed record IdeaJournalResponse(IReadOnlyList<IdeaTradeResponse> Open, IdeaTradesPageResponse Closed, IdeaJournalTotalsResponse Totals, IdeaJournalTotalsResponse PaperTotals)
{
    public static IdeaJournalResponse Of(IdeaJournal journal)
    {
        return new IdeaJournalResponse(
            [.. journal.Open.Select(IdeaTradeResponse.Of)],
            new IdeaTradesPageResponse([.. journal.Closed.Items.Select(IdeaTradeResponse.Of)], journal.Closed.Number, journal.Closed.Size, journal.Closed.Total),
            IdeaJournalTotalsResponse.Of(journal.Totals),
            IdeaJournalTotalsResponse.Of(journal.PaperTotals));
    }
}

/// <summary>A page of trades: its number from 1 (the last page when the one asked for is past the end), its size and the count of all the closed trades of the filter.</summary>
public sealed record IdeaTradesPageResponse(IReadOnlyList<IdeaTradeResponse> Items, int Page, int PageSize, int Total);