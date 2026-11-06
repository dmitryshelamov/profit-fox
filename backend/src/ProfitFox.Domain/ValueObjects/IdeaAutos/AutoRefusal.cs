using ProfitFox.Domain.Entities.IdeaTrades.Enums;

namespace ProfitFox.Domain.ValueObjects.IdeaAutos;

/// <summary>A reason an auto does not take an idea, with what it is about when the reason has anything to tell.</summary>
public sealed record AutoRefusal(IdeaTradeRefusal Reason, RefusalDetail? Detail = null);