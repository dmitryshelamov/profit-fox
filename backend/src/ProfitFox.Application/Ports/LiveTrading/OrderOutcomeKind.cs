namespace ProfitFox.Application.Ports.LiveTrading;

public enum OrderOutcomeKind
{
    /// <summary>The exchange took the order or the cancel, or had taken it already.</summary>
    Accepted,

    /// <summary>A refusal a later attempt may not meet: no answer, the rate limit, a failure of the exchange.</summary>
    Transient,

    /// <summary>A refusal another attempt will meet again: no money, the rules of the instrument, a bad parameter, the key.</summary>
    Permanent,

    /// <summary>A cancel of an order that is done already: filled or cancelled.</summary>
    AlreadyDone
}