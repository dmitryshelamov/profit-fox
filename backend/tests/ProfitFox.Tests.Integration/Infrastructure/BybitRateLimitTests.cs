using System.Globalization;

using Microsoft.Extensions.DependencyInjection;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;

using Shouldly;

namespace ProfitFox.Tests.Integration.Infrastructure;

/// <summary>Decision 7: each answer of Bybit about its rate limit is told from other unavailability, through the replaced HTTP of the app.</summary>
public class BybitRateLimitTests
{
    private ProfitFoxFactory _factory = null!;

    [SetUp]
    public async Task SetUp()
    {
        _factory = await ProfitFoxFactory.CreateAsync(workers: 0);
        _factory.CreateClient().Dispose();
    }

    [TearDown]
    public async Task TearDown()
    {
        await _factory.DisposeAsync();
    }

    [TestCase("429")]
    [TestCase("10006")]
    [TestCase("403")]
    public async Task Answer_over_the_limit_is_a_rate_limit(string answer)
    {
        _factory.Bybit.RateLimit = answer;
        IMarketHistoryClient client = _factory.Services.GetRequiredService<IMarketHistoryClient>();
        var from = DateTimeOffset.Parse("2024-01-01T00:00:00Z", CultureInfo.InvariantCulture);

        await Should.ThrowAsync<ExchangeRateLimitedException>(() =>
            client.GetCandlesOnceAsync(new Instrument(MarketCategory.Spot, "BTCUSDT"), Interval.OneHour, from, from.AddHours(1), CancellationToken.None));
    }
}