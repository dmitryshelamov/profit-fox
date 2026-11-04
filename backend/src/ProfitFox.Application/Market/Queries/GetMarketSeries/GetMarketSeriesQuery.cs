using MediatR;

namespace ProfitFox.Application.Market.Queries.GetMarketSeries;

/// <summary>The points of open interest (in USDT) or the funding settlements of a linear pair in <c>[from, to)</c>, from storage only.</summary>
public sealed record GetMarketSeriesQuery(MarketSeries Series, string Category, string Symbol, string From, string To) : IRequest<GetMarketSeriesResult>;