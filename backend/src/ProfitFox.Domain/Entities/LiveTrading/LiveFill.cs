using ProfitFox.Domain.Entities.Strategies.Enums;

namespace ProfitFox.Domain.Entities.LiveTrading;

/// <summary>One execution of an order of a live bot; <see cref="ExecId" /> is the exchange's id of it.</summary>
public sealed record LiveFill(string ExecId, Guid BotId, int OrderNumber, decimal Quantity, decimal Price, decimal Fee, FeeAsset FeeAsset, DateTimeOffset ExecutedAt);