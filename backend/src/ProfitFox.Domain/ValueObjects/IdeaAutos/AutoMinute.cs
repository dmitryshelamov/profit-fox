using ProfitFox.Domain.Entities.IdeaAutos;

namespace ProfitFox.Domain.ValueObjects.IdeaAutos;

/// <summary>
///     What the autos did in a minute of the scanner, to be written in the transaction of the ideas of the minute under the lease of
///     <paramref name="Owner" />: the paper trades made and stepped, the entry requests of the autos with money and the refusals. What belongs
///     to an idea that storage did not take is left out.
/// </summary>
public sealed record AutoMinute(
    Guid Owner,
    IReadOnlyCollection<PaperIdeaTrade> Opened,
    IReadOnlyCollection<PaperIdeaTrade> Stepped,
    IReadOnlyCollection<IdeaAutoEntry> Entries,
    IReadOnlyCollection<IdeaAutoRefusal> Refusals);