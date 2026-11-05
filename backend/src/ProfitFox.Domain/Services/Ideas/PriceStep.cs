namespace ProfitFox.Domain.Services.Ideas;

/// <summary>A price on the price step of a pair.</summary>
public static class PriceStep
{
    /// <summary>The price on the step at or below it when <paramref name="down" />, at or above it otherwise.</summary>
    public static decimal Round(decimal price, decimal tick, bool down)
    {
        decimal steps = price / tick;

        return (down ? Math.Floor(steps) : Math.Ceiling(steps)) * tick;
    }
}