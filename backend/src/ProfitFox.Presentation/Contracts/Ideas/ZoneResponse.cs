namespace ProfitFox.Presentation.Contracts.Ideas;

/// <summary>A zone from its lowest touch to its highest, its strength the number of touches; <see cref="Kind" /> is <c>support</c> below the price, <c>resistance</c> above it, <c>around</c> when the price stands in it.</summary>
public sealed record ZoneResponse(string Low, string High, int Strength, string Kind);