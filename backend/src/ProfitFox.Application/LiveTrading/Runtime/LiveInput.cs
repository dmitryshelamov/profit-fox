using ProfitFox.Application.Ports.LiveTrading;
using ProfitFox.Domain.Entities.LiveTrading;
using ProfitFox.Domain.Entities.MarketData;

namespace ProfitFox.Application.LiveTrading.Runtime;

/// <summary>Something a live bot reacts to; a session takes its inputs one by one.</summary>
internal abstract record LiveInput;

/// <summary>A closed candle of the instrument of the bot.</summary>
internal sealed record CandleInput(Candle Candle) : LiveInput;

/// <summary>An update of an order of the bot from the exchange.</summary>
internal sealed record OrderInput(ExchangeOrder Order) : LiveInput;

/// <summary>An execution of an order of the bot from the exchange.</summary>
internal sealed record FillInput(ExchangeFill Fill) : LiveInput;

/// <summary>A stream is back after a gap: what came meanwhile is read again.</summary>
internal sealed record RestoreInput : LiveInput;

/// <summary>The bot as the database holds it, read with each renewal of the lease.</summary>
internal sealed record StatusInput(LiveBot Bot) : LiveInput;

/// <summary>The time of a delayed send has come.</summary>
internal sealed record WakeInput : LiveInput;