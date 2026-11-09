using ProfitFox.Domain.Entities.IdeaTrades.Enums;
using ProfitFox.Domain.ValueObjects.IdeaTrades;

namespace ProfitFox.Domain.Entities.IdeaTrades;

/// <summary>
///     The part of one liquidation or auto-deleveraging of the exchange (<see cref="Leg" /> <see cref="IdeaTradeLeg.Liquidation" /> or
///     <see cref="IdeaTradeLeg.Adl" />) that closes a trade; <see cref="ExecId" /> is the exchange's id of the execution. The shares of an execution
///     are written once, and each trade applies its own once.
/// </summary>
public sealed record IdeaTradeForcedShare(string ExecId, Guid TradeId, IdeaTradeLeg Leg, LegTotals Totals, DateTimeOffset ExecutedAt);