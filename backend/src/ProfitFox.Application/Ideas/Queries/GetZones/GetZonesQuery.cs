using MediatR;

namespace ProfitFox.Application.Ideas.Queries.GetZones;

/// <summary>The zones of support and resistance of a pair on an interval of the setups; raw parameters as the user sent them.</summary>
public sealed record GetZonesQuery(string Category, string Symbol, string? Interval) : IRequest<GetZonesResult>;