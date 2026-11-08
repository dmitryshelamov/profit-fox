using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Exchanges;

using Shouldly;

namespace ProfitFox.Tests.Unit.Infrastructure.Exchanges;

public class CachedInstrumentCatalogTests
{
    private static readonly ListedInstrument[] Spot = [new ListedInstrument(MarketCategory.Spot, "BTCUSDT", "BTC", "USDT")];
    private static readonly ListedInstrument[] Linear = [new ListedInstrument(MarketCategory.Linear, "ETHUSDT", "ETH", "USDT")];
    private static readonly ListedInstrument[] Moex = [new ListedInstrument(MarketCategory.Moex, "SBER", "SBER", "RUB", TradFi: true)];

    private IInstrumentCatalog _inner = null!;
    private FakeTimeProvider _time = null!;
    private CachedInstrumentCatalog _catalog = null!;
    private int _resolved;

    [SetUp]
    public void SetUp()
    {
        _inner = Substitute.For<IInstrumentCatalog>();
        _inner.ListAsync(MarketCategory.Spot, Arg.Any<CancellationToken>()).Returns(Spot);
        _inner.ListAsync(MarketCategory.Linear, Arg.Any<CancellationToken>()).Returns(Linear);
        _inner.ListAsync(MarketCategory.Moex, Arg.Any<CancellationToken>()).Returns(Moex);
        _time = new FakeTimeProvider(DateTimeOffset.Parse("2024-03-10T12:30:00Z"));
        _resolved = 0;
        ServiceProvider provider = new ServiceCollection()
            .AddKeyedTransient<IInstrumentCatalog>(CachedInstrumentCatalog.InnerKey, (_, _) =>
            {
                _resolved++;

                return _inner;
            })
            .BuildServiceProvider();
        _catalog = new CachedInstrumentCatalog(provider.GetRequiredService<IServiceScopeFactory>(), _time);
    }

    [Test]
    public async Task Second_list_within_five_minutes_does_not_ask_the_exchange()
    {
        await _catalog.ListAsync(MarketCategory.Spot, CancellationToken.None);
        _time.Advance(TimeSpan.FromMinutes(5) - TimeSpan.FromTicks(1));

        IReadOnlyList<ListedInstrument> second = await _catalog.ListAsync(MarketCategory.Spot, CancellationToken.None);

        second.ShouldBe(Spot);
        await _inner.Received(1).ListAsync(MarketCategory.Spot, Arg.Any<CancellationToken>());
    }

    // Decision 8: the list of moex is kept apart from the lists of Bybit, on the same terms.
    [Test]
    public async Task Moex_is_cached_apart_from_the_categories_of_bybit()
    {
        await _catalog.ListAsync(MarketCategory.Spot, CancellationToken.None);

        (await _catalog.ListAsync(MarketCategory.Moex, CancellationToken.None)).ShouldBe(Moex);
        (await _catalog.ListAsync(MarketCategory.Moex, CancellationToken.None)).ShouldBe(Moex);

        await _inner.Received(1).ListAsync(MarketCategory.Moex, Arg.Any<CancellationToken>());
        await _inner.Received(1).ListAsync(MarketCategory.Spot, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task List_after_five_minutes_asks_the_exchange_again()
    {
        await _catalog.ListAsync(MarketCategory.Spot, CancellationToken.None);
        _time.Advance(TimeSpan.FromMinutes(5));

        await _catalog.ListAsync(MarketCategory.Spot, CancellationToken.None);

        await _inner.Received(2).ListAsync(MarketCategory.Spot, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Each_category_is_cached_on_its_own()
    {
        await _catalog.ListAsync(MarketCategory.Spot, CancellationToken.None);

        IReadOnlyList<ListedInstrument> linear = await _catalog.ListAsync(MarketCategory.Linear, CancellationToken.None);

        linear.ShouldBe(Linear);
    }

    [Test]
    public async Task Failure_is_not_cached()
    {
        _inner.ListAsync(MarketCategory.Spot, Arg.Any<CancellationToken>()).Returns(
            Task.FromException<IReadOnlyList<ListedInstrument>>(new ExchangeUnavailableException("down")),
            Task.FromResult<IReadOnlyList<ListedInstrument>>(Spot));

        await Should.ThrowAsync<ExchangeUnavailableException>(() => _catalog.ListAsync(MarketCategory.Spot, CancellationToken.None));
        IReadOnlyList<ListedInstrument> second = await _catalog.ListAsync(MarketCategory.Spot, CancellationToken.None);

        second.ShouldBe(Spot);
    }

    [Test]
    public async Task Concurrent_lists_on_a_cold_cache_share_one_fetch()
    {
        var fetch = new TaskCompletionSource<IReadOnlyList<ListedInstrument>>();
        _inner.ListAsync(MarketCategory.Spot, Arg.Any<CancellationToken>()).Returns(fetch.Task);

        Task<IReadOnlyList<ListedInstrument>> first = _catalog.ListAsync(MarketCategory.Spot, CancellationToken.None);
        Task<IReadOnlyList<ListedInstrument>> second = _catalog.ListAsync(MarketCategory.Spot, CancellationToken.None);
        fetch.SetResult(Spot);

        (await first).ShouldBe(Spot);
        (await second).ShouldBe(Spot);
        await _inner.Received(1).ListAsync(MarketCategory.Spot, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Failure_of_a_shared_fetch_reaches_every_caller_and_is_not_cached()
    {
        var fetch = new TaskCompletionSource<IReadOnlyList<ListedInstrument>>();
        _inner.ListAsync(MarketCategory.Spot, Arg.Any<CancellationToken>()).Returns(fetch.Task, Task.FromResult<IReadOnlyList<ListedInstrument>>(Spot));

        Task<IReadOnlyList<ListedInstrument>> first = _catalog.ListAsync(MarketCategory.Spot, CancellationToken.None);
        Task<IReadOnlyList<ListedInstrument>> second = _catalog.ListAsync(MarketCategory.Spot, CancellationToken.None);
        fetch.SetException(new ExchangeUnavailableException("down"));

        await Should.ThrowAsync<ExchangeUnavailableException>(() => first);
        await Should.ThrowAsync<ExchangeUnavailableException>(() => second);
        (await _catalog.ListAsync(MarketCategory.Spot, CancellationToken.None)).ShouldBe(Spot);
    }

    [Test]
    public async Task Caller_that_stops_waiting_does_not_cancel_the_fetch_of_the_others()
    {
        var fetch = new TaskCompletionSource<IReadOnlyList<ListedInstrument>>();
        _inner.ListAsync(MarketCategory.Spot, Arg.Any<CancellationToken>()).Returns(fetch.Task);
        using var cancel = new CancellationTokenSource();

        Task<IReadOnlyList<ListedInstrument>> leaving = _catalog.ListAsync(MarketCategory.Spot, cancel.Token);
        Task<IReadOnlyList<ListedInstrument>> staying = _catalog.ListAsync(MarketCategory.Spot, CancellationToken.None);
        await cancel.CancelAsync();
        fetch.SetResult(Spot);

        await Should.ThrowAsync<OperationCanceledException>(() => leaving);
        (await staying).ShouldBe(Spot);
    }

    // The inner catalog holds an HTTP client; kept for the life of the app it would never see a changed address of Bybit.
    [Test]
    public async Task Each_fetch_resolves_the_inner_catalog_anew()
    {
        await _catalog.ListAsync(MarketCategory.Spot, CancellationToken.None);
        await _catalog.ListAsync(MarketCategory.Spot, CancellationToken.None);
        _time.Advance(TimeSpan.FromMinutes(5));
        await _catalog.ListAsync(MarketCategory.Spot, CancellationToken.None);

        _resolved.ShouldBe(2);
    }
}