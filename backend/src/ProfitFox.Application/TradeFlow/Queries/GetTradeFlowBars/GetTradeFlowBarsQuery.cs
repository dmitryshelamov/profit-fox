using MediatR;

namespace ProfitFox.Application.TradeFlow.Queries.GetTradeFlowBars;

/// <summary>The bars of the trade flow of a pair of the market; raw parameters as the user sent them.</summary>
public sealed record GetTradeFlowBarsQuery(string Category, string Symbol, string? Interval, string From, string To) : IRequest<GetTradeFlowBarsResult>;