namespace ProfitFox.Presentation.Contracts.Ideas;

/// <summary>The zones of support and resistance of a pair on an interval, lowest first, and the price they are told by.</summary>
public sealed record ZonesResponse(string Price, IReadOnlyList<ZoneResponse> Zones);