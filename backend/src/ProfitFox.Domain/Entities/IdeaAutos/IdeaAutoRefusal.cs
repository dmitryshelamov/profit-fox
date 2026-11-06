using ProfitFox.Domain.Entities.IdeaAutos.Enums;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.IdeaTrades.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.IdeaAutos;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Domain.Entities.IdeaAutos;

/// <summary>
///     An idea that fit an auto and was not taken: when, by which auto in which mode, the fields of the idea copied for showing (it is deleted a week
///     after it closes), every reason and what the first one is about. One per idea and auto.
/// </summary>
public sealed record IdeaAutoRefusal(
    Guid Id,
    Guid IdeaId,
    Guid AutoId,
    IdeaAutoMode Mode,
    DateTimeOffset At,
    Instrument Instrument,
    IdeaSetup Setup,
    Interval Interval,
    IdeaType Type,
    IdeaSide Side,
    IReadOnlyList<IdeaTradeRefusal> Reasons,
    RefusalDetail? Detail);