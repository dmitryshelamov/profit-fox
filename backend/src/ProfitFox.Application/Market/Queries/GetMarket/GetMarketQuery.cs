using MediatR;

namespace ProfitFox.Application.Market.Queries.GetMarket;

/// <summary>The market snapshot and the state of the scanner, from storage only.</summary>
public sealed record GetMarketQuery : IRequest<GetMarketResult>;