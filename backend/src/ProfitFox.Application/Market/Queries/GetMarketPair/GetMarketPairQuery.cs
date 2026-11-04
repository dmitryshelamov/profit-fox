using MediatR;

namespace ProfitFox.Application.Market.Queries.GetMarketPair;

/// <summary>The row of a pair in the snapshot; raw parameters as the user sent them.</summary>
public sealed record GetMarketPairQuery(string Category, string Symbol) : IRequest<GetMarketPairResult>;