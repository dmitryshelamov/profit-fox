namespace ProfitFox.Domain.ValueObjects.Trades;

/// <summary>
///     The lease of the trading Worker (change take-idea-trades, design.md, decision 2): who holds it, until when, and the
///     time of the database — the Worker works while the lease has not run out.
/// </summary>
public sealed record TraderState(string Worker, DateTimeOffset Until, DateTimeOffset Now)
{
    public bool Alive => Until > Now;
}
