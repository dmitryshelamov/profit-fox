namespace ProfitFox.Engine.Simulation.Market;

/// <summary>
///     A span of time <c>[Start, End)</c>, UTC.
/// </summary>
public sealed record TimeRange(DateTimeOffset Start, DateTimeOffset End)
{
    /// <summary>
    ///     Whether the moment lies in the span.
    /// </summary>
    public bool Contains(DateTimeOffset moment)
    {
        return moment >= Start && moment < End;
    }
}