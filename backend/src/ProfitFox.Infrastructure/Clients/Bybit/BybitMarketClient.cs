using Bybit.Net.Clients;
using Bybit.Net.Enums;
using Bybit.Net.Objects.Models.V5;

using Microsoft.Extensions.Logging;

using ProfitFox.Application.Ports.Exchange;
using ProfitFox.Domain.ValueObjects.Trades;

namespace ProfitFox.Infrastructure.Clients.Bybit;

/// <summary>
///     The public data of Bybit v5 for the trades (change take-idea-trades, design.md, decision 7): <c>/v5/market/tickers</c>
///     for the last and the mark prices and <c>/v5/market/instruments-info</c> for the rules of a pair, in the category
///     <c>linear</c>. No key.
/// </summary>
internal sealed class BybitMarketClient(HttpClient http, BybitSettings settings, ILogger<BybitMarketClient>? logger = null) : IExchangeMarket
{
    private readonly BybitCalls _calls = new BybitCalls(http, settings, logger);

    public async Task<decimal?> PriceAsync(string symbol, CancellationToken cancellationToken)
    {
        using BybitRestClient client = _calls.Client(null);
        BybitResponse<BybitLinearInverseTicker> tickers = _calls.Read("tickers", await client.V5Api.ExchangeData.GetLinearInverseTickersAsync(Category.Linear, symbol, ct: cancellationToken));
        return tickers.List.FirstOrDefault(ticker => ticker.Symbol == symbol) is { LastPrice: > 0 } found ? found.LastPrice : null;
    }

    public async Task<PairRules?> RulesAsync(string symbol, CancellationToken cancellationToken)
    {
        using BybitRestClient client = _calls.Client(null);
        BybitResponse<BybitLinearInverseSymbol> symbols = _calls.Read("instruments-info", await client.V5Api.ExchangeData.GetLinearInverseSymbolsAsync(Category.Linear, symbol, ct: cancellationToken));
        return symbols.List.FirstOrDefault(pair => pair.Name == symbol) is { LotSizeFilter: not null, PriceFilter: not null, LeverageFilter: not null } found
            ? new PairRules(
                found.Name,
                found.LotSizeFilter!.QuantityStep,
                found.LotSizeFilter!.MinOrderQuantity,
                found.LotSizeFilter!.MinNotionalValue ?? 0m,
                found.PriceFilter!.TickSize,
                found.LeverageFilter!.MaxLeverage,
                found.Status == SymbolStatus.Trading)
            : null;
    }

    public async Task<IReadOnlyDictionary<string, decimal>> MarksAsync(IReadOnlyCollection<string> symbols, CancellationToken cancellationToken)
    {
        using BybitRestClient client = _calls.Client(null);
        BybitResponse<BybitLinearInverseTicker> tickers = _calls.Read("tickers", await client.V5Api.ExchangeData.GetLinearInverseTickersAsync(Category.Linear, ct: cancellationToken));
        HashSet<string> wanted = [.. symbols];
        return tickers.List.Where(ticker => wanted.Contains(ticker.Symbol) && ticker.MarkPrice > 0).ToDictionary(ticker => ticker.Symbol, ticker => ticker.MarkPrice);
    }
}
