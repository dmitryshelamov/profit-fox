namespace ProfitFox.Domain.ValueObjects.Ideas;

/// <summary>A zone of support or resistance: from its lowest touch to its highest, its strength the number of touches.</summary>
public sealed record Zone(decimal Low, decimal High, int Strength);