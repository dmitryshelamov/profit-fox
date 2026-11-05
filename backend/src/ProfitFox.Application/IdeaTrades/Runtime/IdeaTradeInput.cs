using ProfitFox.Application.Ports.LiveTrading;
using ProfitFox.Domain.Entities.IdeaTrades;

namespace ProfitFox.Application.IdeaTrades.Runtime;

/// <summary>Something a trade by an idea reacts to; a session takes its inputs one by one.</summary>
internal abstract record IdeaTradeInput;

/// <summary>An update of an order of the trade, or of its stop or target, from the exchange.</summary>
internal sealed record OrderInput(ExchangeOrder Order) : IdeaTradeInput;

/// <summary>An execution of an order of the trade, or of its stop or target, from the exchange.</summary>
internal sealed record FillInput(ExchangeFill Fill) : IdeaTradeInput;

/// <summary>A stream is back after a gap: what came meanwhile is read again.</summary>
internal sealed record RestoreInput : IdeaTradeInput;

/// <summary>The trade as the database holds it, read with each renewal of the lease.</summary>
internal sealed record StatusInput(IdeaTrade Trade) : IdeaTradeInput;

/// <summary>The time of a delayed send, or of the deadline of the entry, has come.</summary>
internal sealed record WakeInput : IdeaTradeInput;