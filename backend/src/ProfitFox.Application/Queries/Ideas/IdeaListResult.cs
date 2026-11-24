using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.ValueObjects.Lists;

namespace ProfitFox.Application.Queries.Ideas;

/// <summary>
///     A page of ideas, and whether the saved filter asked for is gone.
/// </summary>
public sealed record IdeaListResult(ListPage<Idea> Page, bool FilterGone);

/// <summary>
///     A page of the summary of the journal, and whether the saved filter asked for is gone.
/// </summary>
public sealed record IdeaSummaryResult(ListPage<IdeaSummary> Page, bool FilterGone);
