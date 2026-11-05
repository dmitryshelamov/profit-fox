namespace ProfitFox.Domain.ValueObjects.Ideas;

/// <summary>How touches make zones: touches within <see cref="Merge" /> ATR of the lowest one join it, a zone needs <see cref="Strength" /> touches.</summary>
public sealed record ZoneRules(decimal Merge, int Strength)
{
    public static readonly ZoneRules Default = new ZoneRules(0.5m, 2);
}