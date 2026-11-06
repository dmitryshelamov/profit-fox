using ProfitFox.Domain.Entities.IdeaAutos;
using ProfitFox.Domain.Entities.IdeaTrades;

namespace ProfitFox.Domain.ValueObjects.IdeaAutos;

/// <summary>
///     The trades an auto is checked against: the open real trades of every account (manual and of autos), the open paper trades, the autos by id
///     with the deleted ones (for names and places), and the name of the account of the auto when it is known.
/// </summary>
public sealed record AutoBooks(
    IReadOnlyCollection<IdeaTrade> OpenReal,
    IReadOnlyCollection<PaperIdeaTrade> OpenPaper,
    IReadOnlyDictionary<Guid, IdeaAuto> Autos,
    string? AccountName = null);