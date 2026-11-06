using ProfitFox.Application.Paging;
using ProfitFox.Domain.ValueObjects.IdeaTrades;

namespace ProfitFox.Application.IdeaTrades.Queries.GetIdeaJournal;

/// <summary>The journal, or validation errors keyed by parameter name.</summary>
public sealed record GetIdeaJournalResult(IdeaJournal? Journal, IReadOnlyDictionary<string, string> Errors);

/// <summary>The open trades, the newest first, a page of the closed ones by the time they closed, the totals of all the closed real ones and those of the paper ones.</summary>
public sealed record IdeaJournal(IReadOnlyList<IdeaJournalTrade> Open, Page<IdeaJournalTrade> Closed, IdeaJournalTotals Totals, IdeaJournalTotals PaperTotals);