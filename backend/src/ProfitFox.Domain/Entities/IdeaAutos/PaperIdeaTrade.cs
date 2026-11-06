using ProfitFox.Domain.Entities.IdeaTrades;

namespace ProfitFox.Domain.Entities.IdeaAutos;

/// <summary>
///     A paper trade of an auto without money: the <see cref="IdeaTrade" /> its entry and exits fill the legs of, the auto that took it, and the
///     open time of the next minute of its pair to step it by.
/// </summary>
public sealed record PaperIdeaTrade(IdeaTrade Trade, Guid AutoId, DateTimeOffset SteppedUntil);