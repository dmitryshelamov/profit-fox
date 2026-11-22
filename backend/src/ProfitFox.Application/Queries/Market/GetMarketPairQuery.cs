using MediatR;

using ProfitFox.Domain.ValueObjects.Lists;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.Queries.Market;

/// <summary>
///     A pair of the set of pairs with its snapshot (spec market-overview «Страница пары»); null for a pair outside the set.
/// </summary>
public sealed record GetMarketPairQuery(string Symbol) : IRequest<MarketPairInternal?>;

/// <summary>
///     The pair and its row of the market, null before the scanner wrote one.
/// </summary>
public sealed record MarketPairInternal(Instrument Pair, MarketPairRow? Row);
