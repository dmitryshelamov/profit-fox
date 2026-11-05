using MediatR;

namespace ProfitFox.Application.Ideas.Queries.GetIdeas;

/// <summary>
///     The open ideas and the ones closed in the last 24 hours, by expectancy, highest first; each filter is a code as the user sent it, none
///     takes every idea.
/// </summary>
public sealed record GetIdeasQuery(string? Setup, string? Interval, string? Type, string? Side, string? Category, bool NonNegativeExpectancy) : IRequest<GetIdeasResult>;