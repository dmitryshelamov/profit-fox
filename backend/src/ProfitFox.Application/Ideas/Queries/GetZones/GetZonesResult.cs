using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Application.Ideas.Queries.GetZones;

/// <summary>The price of the pair and its zones, lowest first; <see cref="NotFound" /> when the snapshot has no such pair; or validation errors.</summary>
public sealed record GetZonesResult(decimal? Price, IReadOnlyList<Zone>? Zones, bool NotFound, IReadOnlyDictionary<string, string> Errors);