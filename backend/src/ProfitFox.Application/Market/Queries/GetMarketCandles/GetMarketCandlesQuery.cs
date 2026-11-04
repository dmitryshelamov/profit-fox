using MediatR;

namespace ProfitFox.Application.Market.Queries.GetMarketCandles;

/// <summary>Closed candles of a pair of the market; raw parameters as the user sent them.</summary>
public sealed record GetMarketCandlesQuery(string Category, string Symbol, string? Interval, string From, string To) : IRequest<GetMarketCandlesResult>;