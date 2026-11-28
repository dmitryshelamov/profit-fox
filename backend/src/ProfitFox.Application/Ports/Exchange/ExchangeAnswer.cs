namespace ProfitFox.Application.Ports.Exchange;

/// <summary>
///     How the exchange answered a write (change take-idea-trades, design.md, decision 4): taken; the link id of the order
///     was taken before (<c>110072</c>); the leverage was already that (<c>110043</c>); for now — the exchange did not answer,
///     answered 403 or 5xx, the limit of requests, or <c>10000</c>, <c>10006</c>, <c>10016</c> —, so the write is tried again;
///     or refused for good.
/// </summary>
public enum ExchangeAnswerKind
{
    Accepted,
    Duplicate,
    Unchanged,
    Temporary,
    Refused
}

/// <summary>
///     The answer of the exchange to a write: its kind, the code and the reason of Bybit, and the id of the order it took.
/// </summary>
public sealed record ExchangeAnswer(ExchangeAnswerKind Kind, string? Code = null, string? Reason = null, string? OrderId = null)
{
    public static readonly ExchangeAnswer Accepted = new ExchangeAnswer(ExchangeAnswerKind.Accepted);

    /// <summary>
    ///     Taken now or before: the order is at the exchange.
    /// </summary>
    public bool Taken => Kind is ExchangeAnswerKind.Accepted or ExchangeAnswerKind.Duplicate or ExchangeAnswerKind.Unchanged;
}
