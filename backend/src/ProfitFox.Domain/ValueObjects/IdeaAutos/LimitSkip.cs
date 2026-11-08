using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.IdeaTrades.Enums;

namespace ProfitFox.Domain.ValueObjects.IdeaAutos;

/// <summary>
///     The ideas of a side a limit of an auto skipped in a minute (decision 4 of add-burst-and-auto-limits): the first of them, the limit that
///     skipped them, how many it skipped and the limit.
/// </summary>
public sealed record LimitSkip(Idea First, IdeaTradeRefusal Reason, int Skipped, int Limit);