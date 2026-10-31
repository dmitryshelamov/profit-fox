using MediatR;

namespace ProfitFox.Application.MarketData.Queries.GetCandles;

/// <summary>Raw read parameters as the user sent them; <see cref="Limit"/> defaults to <see cref="GetCandlesQueryHandler.DefaultLimit"/>.</summary>
public sealed record GetCandlesQuery(string Category, string Symbol, string Interval, string From, string To, int? Limit)
    : IRequest<GetCandlesResult>;
