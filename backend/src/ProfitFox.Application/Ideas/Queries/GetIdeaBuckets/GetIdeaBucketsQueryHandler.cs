using MediatR;

using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Application.Ideas.Queries.GetIdeaBuckets;

public sealed class GetIdeaBucketsQueryHandler(IIdeaBucketRepository buckets, IdeasEngineVersion version) : IRequestHandler<GetIdeaBucketsQuery, IReadOnlyList<IdeaBucket>>
{
    public Task<IReadOnlyList<IdeaBucket>> Handle(GetIdeaBucketsQuery request, CancellationToken cancellationToken)
    {
        return buckets.GetAsync(version.Number, cancellationToken);
    }
}