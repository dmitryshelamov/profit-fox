using ProfitFox.Domain.Entities.IdeaAutos;
using ProfitFox.Domain.ValueObjects.IdeaAutos;

namespace ProfitFox.Application.IdeaAutos;

/// <summary>
///     The entry of an auto with money, for <see cref="IdeaTrades.IdeaTradeEntryCalculator.EvaluateAsync" />: the auto, the lowest reward to risk it asked
///     in the minute of the idea (which replaces the one of the settings), the daily stop as it stands, the open paper trades and the autos by id.
/// </summary>
public sealed record AutoEntry(IdeaAuto Auto, decimal MinRiskReward, DailyLossState DailyLoss, IReadOnlyCollection<PaperIdeaTrade> OpenPaper, IReadOnlyDictionary<Guid, IdeaAuto> Autos);