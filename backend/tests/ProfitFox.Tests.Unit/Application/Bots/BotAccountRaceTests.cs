using Microsoft.Extensions.Time.Testing;

using NSubstitute;
using NSubstitute.ExceptionExtensions;

using ProfitFox.Application.Bots.Commands.CreateBot;
using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Application.Strategies;
using ProfitFox.Domain.Entities.Bots.Enums;
using ProfitFox.Domain.Entities.ExchangeAccounts;
using ProfitFox.Domain.Entities.ExchangeAccounts.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Bots;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Tests.Shared.Fixtures.Strategies;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.Bots;

public class BotAccountRaceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2024-03-10T12:00:00Z");

    // Requirement "Проверка аккаунта бота": the account is deleted after the bot was checked against it.
    [Test]
    public async Task Bot_whose_account_is_deleted_while_saving_is_an_error_of_the_account()
    {
        var library = new StrategyLibrary(0, [], []);
        Strategy ema = library.AddStrategy(Guid.NewGuid(), "EMA", StrategyJson.ParseStrategy(StrategyFixtures.Element("ema")), Now);
        IStrategyLibraryRepository repository = Substitute.For<IStrategyLibraryRepository>();
        repository.LoadAsync(Arg.Any<CancellationToken>()).Returns(library);
        repository.SaveAsync(Arg.Any<StrategyLibraryChange>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new BotValidationException([new BotError(BotField.Account, null, "No such exchange account.")]));
        var account = new ExchangeAccount(Guid.NewGuid(), "Main", ExchangeEnvironment.Testnet, "key", 100, new HashSet<MarketCategory> { MarketCategory.Spot }, Now);
        IExchangeAccountRepository accounts = Substitute.For<IExchangeAccountRepository>();
        accounts.GetAsync(account.Id, Arg.Any<CancellationToken>()).Returns(account);
        IMarketHistoryClient marketHistory = Substitute.For<IMarketHistoryClient>();
        marketHistory.InstrumentExistsAsync(Arg.Any<Instrument>(), Arg.Any<CancellationToken>()).Returns(true);
        var handler = new CreateBotCommandHandler(repository, accounts, marketHistory, new FakeTimeProvider(Now));

        ValidationFailedException exception = await Should.ThrowAsync<ValidationFailedException>(() =>
            handler.Handle(new CreateBotCommand("BTC EMA", ema.Id, null, "spot", "BTCUSDT", "60", 1000m, account.Id), CancellationToken.None));

        exception.Errors.ShouldBe(new Dictionary<string, string> { ["exchangeAccountId"] = "No such exchange account." });
    }
}