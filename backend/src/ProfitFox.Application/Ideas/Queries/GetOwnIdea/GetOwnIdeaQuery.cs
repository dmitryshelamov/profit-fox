using MediatR;

namespace ProfitFox.Application.Ideas.Queries.GetOwnIdea;

/// <summary>
///     An idea of a pair of the type, interval and side the owner chose, from the price now by the rules of the type, without a setup; it is
///     not stored and not counted (requirement "Своя идея"). Raw parameters as the user sent them.
/// </summary>
public sealed record GetOwnIdeaQuery(string Category, string Symbol, string? Type, string? Interval, string? Side) : IRequest<GetOwnIdeaResult>;