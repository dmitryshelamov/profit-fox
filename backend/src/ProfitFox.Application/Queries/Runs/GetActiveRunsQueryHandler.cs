using MediatR;

using ProfitFox.Application.Contracts.Runs;
using ProfitFox.Application.Contracts.Runs.Converters;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.Queries.Runs;

internal sealed class GetActiveRunsQueryHandler(IRunRepository runs) : IRequestHandler<GetActiveRunsQuery, IReadOnlyList<RunInternal>>
{
    public async Task<IReadOnlyList<RunInternal>> Handle(GetActiveRunsQuery request, CancellationToken cancellationToken)
    {
        return [.. (await runs.ActiveAsync(cancellationToken)).Select(run => run.ToInternal())];
    }
}
