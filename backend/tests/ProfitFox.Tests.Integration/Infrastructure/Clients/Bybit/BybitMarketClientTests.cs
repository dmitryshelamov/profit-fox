using ProfitFox.Domain.ValueObjects.Trades;
using ProfitFox.Infrastructure.Clients.Bybit;

namespace ProfitFox.Tests.Integration.Infrastructure.Clients.Bybit;

/// <summary>
///     Change take-idea-trades, task 5.2 (design.md, decision 7): the last price and the rules of a pair from
///     <c>/v5/market/tickers</c> and <c>/v5/market/instruments-info</c> of the category <c>linear</c>, without a key.
/// </summary>
public sealed class BybitMarketClientTests
{
    private const string Ticker =
        """{"symbol":"SOLUSDT","lastPrice":"237.40","indexPrice":"237.35","markPrice":"237.41","prevPrice24h":"230.10","price24hPcnt":"0.031725","highPrice24h":"240.00","lowPrice24h":"229.50","prevPrice1h":"236.90","openInterest":"1024000","openInterestValue":"243108840","turnover24h":"1000000000","volume24h":"4200000","fundingRate":"0.0001","nextFundingTime":"1795881600000","predictedDeliveryPrice":"","basisRate":"","deliveryFeeRate":"","deliveryTime":"0","ask1Size":"10.5","bid1Price":"237.39","ask1Price":"237.40","bid1Size":"5.2","basis":""}""";

    private const string Instrument =
        """{"symbol":"SOLUSDT","contractType":"LinearPerpetual","status":"Trading","baseCoin":"SOL","quoteCoin":"USDT","launchTime":"1634256000000","deliveryTime":"0","deliveryFeeRate":"","priceScale":"3","leverageFilter":{"minLeverage":"1","maxLeverage":"50.00","leverageStep":"0.01"},"priceFilter":{"minPrice":"0.010","maxPrice":"19999.980","tickSize":"0.010"},"lotSizeFilter":{"maxOrderQty":"79770.0","minOrderQty":"0.1","qtyStep":"0.1","postOnlyMaxOrderQty":"79770.0","maxMktOrderQty":"16200.0","minNotionalValue":"5"},"unifiedMarginTrade":true,"fundingInterval":480,"settleCoin":"USDT","copyTrading":"both","upperFundingRate":"0.04","lowerFundingRate":"-0.04","isPreListing":false,"preListingInfo":null}""";

    private StubHttpHandler _bybit = null!;

    [SetUp]
    public void Stub()
    {
        _bybit = new StubHttpHandler();
    }

    [TearDown]
    public void Drop()
    {
        _bybit.Dispose();
    }

    [Test]
    public async Task The_price_is_the_last_price_of_the_ticker_of_the_pair()
    {
        _bybit.Json(BybitSamples.List("linear", "", Ticker));

        decimal? price = await Client().PriceAsync("SOLUSDT", CancellationToken.None);

        Assert.That(price, Is.EqualTo(237.40m));
        Uri uri = _bybit.Requests.Single().Uri;
        Assert.That(uri.AbsolutePath, Is.EqualTo("/v5/market/tickers"));
        Assert.That(uri.Query, Does.Contain("category=linear").And.Contain("symbol=SOLUSDT"));
        Assert.That(_bybit.Requests.Single().Headers.Keys, Does.Not.Contain("X-BAPI-API-KEY"));
    }

    [Test]
    public async Task The_rules_are_the_steps_the_minimums_the_greatest_leverage_and_the_status()
    {
        _bybit.Json(BybitSamples.List("linear", "", Instrument));

        PairRules? rules = await Client().RulesAsync("SOLUSDT", CancellationToken.None);

        Assert.That(rules, Is.EqualTo(new PairRules("SOLUSDT", 0.1m, 0.1m, 5m, 0.01m, 50m, true)));
        Assert.That(_bybit.Requests.Single().Uri.AbsolutePath, Is.EqualTo("/v5/market/instruments-info"));
    }

    [Test]
    public async Task A_pair_that_settles_does_not_trade_and_an_unknown_pair_has_no_rules()
    {
        _bybit.Json(BybitSamples.List("linear", "", Instrument.Replace("\"status\":\"Trading\"", "\"status\":\"Settling\""))).Json(BybitSamples.List("linear", ""));

        Assert.That((await Client().RulesAsync("SOLUSDT", CancellationToken.None))!.Trading, Is.False);
        Assert.That(await Client().RulesAsync("NOPEUSDT", CancellationToken.None), Is.Null);
    }

    [Test]
    public async Task The_marks_are_those_of_the_pairs_asked_for()
    {
        _bybit.Json(BybitSamples.List("linear", "", Ticker, Ticker.Replace("SOLUSDT", "XRPUSDT").Replace("\"markPrice\":\"237.41\"", "\"markPrice\":\"1.4126\"")));

        IReadOnlyDictionary<string, decimal> marks = await Client().MarksAsync(["XRPUSDT"], CancellationToken.None);

        Assert.That(marks, Is.EqualTo(new Dictionary<string, decimal> { ["XRPUSDT"] = 1.4126m }));
    }

    private BybitMarketClient Client()
    {
        return new BybitMarketClient(_bybit.Client(), new BybitSettings());
    }
}
