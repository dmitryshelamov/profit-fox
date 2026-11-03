using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using ProfitFox.Application.LiveTrading;
using ProfitFox.Application.LiveTrading.Commands.ClaimLiveBot;
using ProfitFox.Application.Workers;
using ProfitFox.Domain.Repositories;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.LiveTrading.Commands.ClaimLiveBot;

public class ClaimLiveBotCommandHandlerTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-03T10:00:00Z");

    // Requirement "Настройки аренды бота": a bot is leased by the trading settings, not by those of backtest chunks.
    [Test]
    public async Task Bot_is_leased_for_the_lease_of_the_trading_settings()
    {
        ILiveBotRepository repository = Substitute.For<ILiveBotRepository>();
        var worker = new WorkerIdentity(Guid.NewGuid());
        var handler = new ClaimLiveBotCommandHandler(
            repository,
            new TradingSettings { LeaseDuration = TimeSpan.FromMinutes(5) },
            worker,
            new FakeTimeProvider(Now));

        await handler.Handle(new ClaimLiveBotCommand(), CancellationToken.None);

        await repository.Received(1).ClaimAsync(worker.Id, Now, Now + TimeSpan.FromMinutes(5), Arg.Any<CancellationToken>());
    }

    [Test]
    public void Bot_lease_lasts_five_minutes_and_is_renewed_each_minute_by_default()
    {
        var settings = new TradingSettings();

        (settings.LeaseDuration, settings.LeaseRenewal).ShouldBe((TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(1)));
    }
}