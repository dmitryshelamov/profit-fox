namespace ProfitFox.Domain.ValueObjects.Trades;

/// <summary>
///     What the exchange tells of an account (change take-idea-trades, design.md, decision 3): the update of an order, a fill,
///     or the snapshot of a position — the same three by the stream and by the reads of REST, so one handler takes both.
/// </summary>
public abstract record ExchangeMessage(string Symbol);
