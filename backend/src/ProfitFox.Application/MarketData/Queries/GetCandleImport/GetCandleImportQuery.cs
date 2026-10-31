using MediatR;

using ProfitFox.Domain.Entities.MarketData;

namespace ProfitFox.Application.MarketData.Queries.GetCandleImport;

/// <summary>The import job with this id, or <c>null</c> when there is none.</summary>
public sealed record GetCandleImportQuery(Guid Id) : IRequest<CandleImportJob?>;
