using MediatR;

using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.MarketData.Queries.GetCandleImport;

public sealed class GetCandleImportQueryHandler(ICandleImportJobRepository jobs) : IRequestHandler<GetCandleImportQuery, CandleImportJob?>
{
    public Task<CandleImportJob?> Handle(GetCandleImportQuery request, CancellationToken cancellationToken)
    {
        return jobs.GetAsync(request.Id, cancellationToken);
    }
}