using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;

using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Repositories;
using ProfitFox.Presentation.Contracts.Errors;
using ProfitFox.Presentation.Contracts.Ideas;
using ProfitFox.Presentation.Contracts.Trades;
using ProfitFox.Tests.Integration.Application.Commands.Trades;
using ProfitFox.Tests.Integration.MarketData;

namespace ProfitFox.Tests.Integration.Presentation.Controllers;

/// <summary>
///     Change take-idea-trades, tasks 6.3 and 8.1 (design.md, decision 13): <c>/api/trades</c> on the test API with
///     <see cref="Exchange.FakeBybit" /> — the count and the entry with 201, 404, 422 and 503, the lists of the open and the
///     closed trades with pages and sorting, the close with 409, the trade of an idea in the list of ideas, and the removal of
///     an account with a trade.
/// </summary>
[NonParallelizable]
public sealed class TradesControllerTests
{
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    private MarketDataServer _market = null!;
    private TradeBench _bench = null!;
    private BenchTestApplication _api = null!;

    [SetUp]
    public async Task Start()
    {
        _bench = await TradeBench.Start();
        _market = await MarketDataServer.Start();
        _api = new BenchTestApplication(_market, bybit: _bench.Bybit);
    }

    [TearDown]
    public async Task Stop()
    {
        await _api.DisposeAsync();
        await _market.DisposeAsync();
        await _bench.DisposeAsync();
    }

    [Test]
    public async Task The_count_of_an_entry_gives_the_numbers_and_every_check()
    {
        Idea idea = await _bench.Idea();

        EntryPreviewResponse preview = (await (await _api.Http.PostAsJsonAsync("/api/trades/preview", new EntryRequest(idea.Id, null, null))).Content.ReadFromJsonAsync<EntryPreviewResponse>(Json))!;

        Assert.That((preview.Side, preview.Quantity, preview.Stop, preview.StopSource, preview.Passed), Is.EqualTo(("long", 0.4m, (decimal?)231.38m, "idea", true)));
        Assert.That(preview.Checks, Has.Count.EqualTo(19));
        Assert.That((preview.AccountName, preview.Free), Is.EqualTo(("Основной", (decimal?)1250.40m)));
    }

    [Test]
    public async Task An_idea_or_an_account_that_is_not_there_is_404_and_Bybit_that_does_not_answer_is_503()
    {
        Idea idea = await _bench.Idea();

        Assert.That((await _api.Http.PostAsJsonAsync("/api/trades/preview", new EntryRequest(Guid.NewGuid(), null, null))).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await _api.Http.PostAsJsonAsync("/api/trades", new EntryRequest(idea.Id, Guid.NewGuid(), null))).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        _bench.Bybit.Key(TradeBench.MainKey).Unavailable = true;
        Assert.That((await _api.Http.PostAsJsonAsync("/api/trades/preview", new EntryRequest(idea.Id, null, null))).StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
    }

    // 201 with the trade as the list gives it — the moment to the microsecond —, then 422 with the check that failed.
    [Test]
    public async Task An_entry_answers_201_with_the_trade_as_the_list_gives_it_and_a_second_on_the_pair_422()
    {
        Idea idea = await _bench.Idea();

        HttpResponseMessage created = await _api.Http.PostAsJsonAsync("/api/trades", new EntryRequest(idea.Id, null, null));
        HttpResponseMessage second = await _api.Http.PostAsJsonAsync("/api/trades", new EntryRequest(idea.Id, null, null));

        Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        TradeResponse trade = (await created.Content.ReadFromJsonAsync<TradeResponse>(Json))!;
        Assert.That(created.Headers.Location!.AbsolutePath, Is.EqualTo($"/api/trades/{trade.Id}"));
        Assert.That((trade.State, trade.Amount, trade.Leverage, trade.AccountName), Is.EqualTo(("pending", 100m, 3, "Основной")));
        TradePageResponse open = (await _api.Http.GetFromJsonAsync<TradePageResponse>("/api/trades?state=open", Json))!;
        Assert.That(JsonSerializer.Serialize(open.Items.Single(), Json), Is.EqualTo(JsonSerializer.Serialize(trade, Json)));
        Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
        EntryRefusedResponse refused = (await second.Content.ReadFromJsonAsync<EntryRefusedResponse>(Json))!;
        Assert.That(refused.Checks, Is.EqualTo(new[] { new EntryCheckResponse("pairTrade", false, "По паре уже есть сделка") }));
    }

    [Test]
    public async Task The_lists_go_by_pages_and_sort_and_a_wrong_field_is_422()
    {
        foreach (string symbol in new[] { "SOLUSDT", "XRPUSDT" })
        {
            Idea idea = symbol == "SOLUSDT" ? await _bench.Idea() : await _bench.Idea(symbol, ProfitFox.Domain.ValueObjects.Graph.Enums.EntrySide.Short, 1.4051, null, null);
            await _api.Http.PostAsJsonAsync("/api/trades", new EntryRequest(idea.Id, null, null));
        }

        TradePageResponse page = (await _api.Http.GetFromJsonAsync<TradePageResponse>("/api/trades?state=open&sort=symbol&direction=desc&pageSize=50", Json))!;
        TradePageResponse closed = (await _api.Http.GetFromJsonAsync<TradePageResponse>("/api/trades?state=closed", Json))!;
        HttpResponseMessage wrong = await _api.Http.GetAsync("/api/trades?sort=nope");

        Assert.That(page.Items.Select(trade => trade.Symbol), Is.EqualTo(new[] { "XRPUSDT", "SOLUSDT" }));
        Assert.That((page.Total, page.Open, page.Closed, closed.Total), Is.EqualTo((2, 2, 0, 0)));
        Assert.That(wrong.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
        Assert.That((await wrong.Content.ReadFromJsonAsync<ErrorResponse>(Json))!.Errors.Single().Field, Is.EqualTo("sort"));
    }

    [Test]
    public async Task A_trade_waiting_for_its_entry_does_not_close_409_and_an_unknown_one_404()
    {
        Idea idea = await _bench.Idea();
        TradeResponse trade = (await (await _api.Http.PostAsJsonAsync("/api/trades", new EntryRequest(idea.Id, null, null))).Content.ReadFromJsonAsync<TradeResponse>(Json))!;

        HttpResponseMessage pending = await _api.Http.PostAsync($"/api/trades/{trade.Id}/close", null);
        HttpResponseMessage unknown = await _api.Http.PostAsync($"/api/trades/{Guid.NewGuid()}/close", null);
        HttpResponseMessage resolve = await _api.Http.PostAsync($"/api/trades/{trade.Id}/resolve", null);

        Assert.That((pending.StatusCode, unknown.StatusCode, resolve.StatusCode), Is.EqualTo((HttpStatusCode.Conflict, HttpStatusCode.NotFound, HttpStatusCode.Conflict)));
    }

    // 12.20 (R1): «Разобрана» of a trade whose key Bybit no longer takes answers 409 with the way out, and «без проверки» 200.
    [Test]
    public async Task Resolve_without_a_check_is_offered_by_409_and_taken_by_the_flag()
    {
        Idea idea = await _bench.Idea();
        TradeResponse trade = (await (await _api.Http.PostAsJsonAsync("/api/trades", new EntryRequest(idea.Id, null, null))).Content.ReadFromJsonAsync<TradeResponse>(Json))!;
        await _bench.Services.GetRequiredService<ITradeRepository>().ChangeAsync(trade.Id, null, book =>
        {
            book.Trade.Fail("позиция на бирже больше сделки");
            return true;
        }, null, CancellationToken.None);
        _bench.Bybit.Key(TradeBench.MainKey).Refusal = () => throw new ProfitFox.Application.Exceptions.ExchangeKeyRejectedException("API key is invalid.");

        HttpResponseMessage refused = await _api.Http.PostAsync($"/api/trades/{trade.Id}/resolve", null);
        HttpResponseMessage resolved = await _api.Http.PostAsync($"/api/trades/{trade.Id}/resolve?unchecked=true", null);

        Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        JsonElement problem = (await refused.Content.ReadFromJsonAsync<JsonElement>(Json))!;
        Assert.That(problem.GetProperty("canSkipCheck").GetBoolean(), Is.True);
        Assert.That(problem.GetProperty("detail").GetString(), Does.StartWith("Позицию не проверить: Bybit не принимает ключ аккаунта"));
        Assert.That(resolved.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await resolved.Content.ReadFromJsonAsync<TradeResponse>(Json))!.Reason, Does.StartWith("разобрана без проверки"));
    }

    // Spec «Уже в сделке»: the idea with a trade names its state.
    [Test]
    public async Task An_idea_with_a_trade_gives_its_state_in_the_list_of_ideas()
    {
        Idea entered = await _bench.Idea();
        Idea other = await _bench.Idea("XRPUSDT");
        TradeResponse trade = (await (await _api.Http.PostAsJsonAsync("/api/trades", new EntryRequest(entered.Id, null, null))).Content.ReadFromJsonAsync<TradeResponse>(Json))!;

        IdeaPageResponse ideas = (await _api.Http.GetFromJsonAsync<IdeaPageResponse>("/api/ideas?state=open", Json))!;

        Assert.That(ideas.Items.Single(idea => idea.Id == entered.Id).Trade, Is.EqualTo(new IdeaTradeResponse(trade.Id, "pending")));
        Assert.That(ideas.Items.Single(idea => idea.Id == other.Id).Trade, Is.Null);
    }

    // Change take-idea-trades, task 6.3, spec «Открытая сделка» and «Только закрытые сделки».
    [Test]
    public async Task An_account_with_an_open_trade_is_not_removed_409_and_its_closed_trades_keep_its_name()
    {
        Idea idea = await _bench.Idea();
        TradeResponse trade = (await (await _api.Http.PostAsJsonAsync("/api/trades", new EntryRequest(idea.Id, null, null))).Content.ReadFromJsonAsync<TradeResponse>(Json))!;

        HttpResponseMessage refused = await _api.Http.DeleteAsync($"/api/accounts/{_bench.Main.Id}");
        Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));

        await _bench.Services.GetRequiredService<ITradeRepository>().ChangeAsync(trade.Id, null, book =>
        {
            book.Trade.Cancel("вход не ушёл вовремя", DateTimeOffset.UtcNow);
            return true;
        }, null, CancellationToken.None);
        HttpResponseMessage removed = await _api.Http.DeleteAsync($"/api/accounts/{_bench.Main.Id}");

        Assert.That(removed.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        TradePageResponse closed = (await _api.Http.GetFromJsonAsync<TradePageResponse>("/api/trades?state=closed", Json))!;
        Assert.That(closed.Items.Select(item => (item.AccountId, item.AccountName, item.State)), Is.EqualTo(new[] { ((Guid?)null, "Основной", "cancelled") }));
    }
}
