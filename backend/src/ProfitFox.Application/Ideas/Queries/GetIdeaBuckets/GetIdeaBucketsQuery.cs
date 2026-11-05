using MediatR;

using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Application.Ideas.Queries.GetIdeaBuckets;

/// <summary>The statistics of every bucket of the current version of the rules, over 90 and over the last 30 days.</summary>
public sealed record GetIdeaBucketsQuery : IRequest<IReadOnlyList<IdeaBucket>>;