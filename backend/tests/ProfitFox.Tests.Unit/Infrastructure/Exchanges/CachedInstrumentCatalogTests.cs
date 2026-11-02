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

    private IInstrumentCatalog _inner = null!;
    private FakeTimeProvider _time = null!;
    private CachedInstrumentCatalog _catalog = null!;

    [SetUp]
    public void SetUp()
    {
        _inner = Substitute.For<IInstrumentCatalog>();
        _inner.ListAsync(MarketCategory.Spot, Arg.Any<CancellationToken>()).Returns(Spot);
        _inner.ListAsync(MarketCategory.Linear, Arg.Any<CancellationToken>()).Returns(Linear);
        _time = new FakeTimeProvider(DateTimeOffset.Parse("2024-03-10T12:30:00Z"));
        _catalog = new CachedInstrumentCatalog(_inner, _time);
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
}