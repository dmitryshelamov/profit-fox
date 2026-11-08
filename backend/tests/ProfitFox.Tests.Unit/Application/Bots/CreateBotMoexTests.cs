using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using ProfitFox.Application.Bots.Commands.CreateBot;
using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Application.Strategies;
using ProfitFox.Domain.Entities.ExchangeAccounts;
using ProfitFox.Domain.Entities.ExchangeAccounts.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Exchanges.Routing;
using ProfitFox.Tests.Shared.Fixtures.Strategies;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.Bots;

/// <summary>Scenarios "Акция MOEX" and "Акция не TQBR" of the bot: the symbol of moex is checked at T-Invest.</summary>
public class CreateBotMoexTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2024-03-10T12:00:00Z");

    private IStrategyLibraryRepository _repository = null!;
    private IExchangeAccountRepository _accounts = null!;
    private IMarketHistoryClient _bybit = null!;
    private IMarketHistoryClient _tinvest = null!;
    private CreateBotCommandHandler _handler = null!;
    private Strategy _ema = null!;
    private Strategy _emaShort = null!;

    [SetUp]
    public void SetUp()
    {
        var library = new StrategyLibrary(0, [], []);
        _ema = library.AddStrategy(Guid.NewGuid(), "EMA", StrategyJson.ParseStrategy(StrategyFixtures.Element("ema")), Now);
        _emaShort = library.AddStrategy(Guid.NewGuid(), "EMA Short", StrategyJson.ParseStrategy(StrategyFixtures.Element("ema-short")), Now);
        _accounts = Substitute.For<IExchangeAccountRepository>();
        _repository = Substitute.For<IStrategyLibraryRepository>();
        _repository.LoadAsync(Arg.Any<CancellationToken>()).Returns(library);
        _repository.SaveAsync(Arg.Any<StrategyLibraryChange>(), Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(true);
        _bybit = Substitute.For<IMarketHistoryClient>();
        _tinvest = Substitute.For<IMarketHistoryClient>();
        _tinvest.InstrumentExistsAsync(new Instrument(MarketCategory.Moex, "SBER"), Arg.Any<CancellationToken>()).Returns(true);
        _handler = new CreateBotCommandHandler(_repository, _accounts, new RoutedMarketHistoryClient(_bybit, _tinvest), new FakeTimeProvider(Now));
    }

    // Scenario "Шорт на MOEX": the margin of MOEX is a later change, so a short strategy trades on linear only.
    [Test]
    public async Task Short_strategy_on_moex_is_an_error_of_the_category()
    {
        ValidationFailedException error = await Should.ThrowAsync<ValidationFailedException>(() =>
            _handler.Handle(new CreateBotCommand("SBER short", _emaShort.Id, null, "moex", "SBER", "60", 10000m, null), CancellationToken.None));

        error.Errors.Keys.ShouldBe(["category"]);
    }

    // Scenario "Аккаунт Bybit у бота на MOEX": a key of Bybit trades in spot and linear, never in moex.
    [Test]
    public async Task Bybit_account_on_a_moex_bot_is_an_error_of_the_account()
    {
        var bybit = new ExchangeAccount(Guid.NewGuid(), "Main", "key", 100, new HashSet<MarketCategory> { MarketCategory.Spot, MarketCategory.Linear }, Now);
        _accounts.GetAsync(bybit.Id, Arg.Any<CancellationToken>()).Returns(bybit);

        ValidationFailedException error = await Should.ThrowAsync<ValidationFailedException>(() =>
            _handler.Handle(new CreateBotCommand("SBER EMA", _ema.Id, null, "moex", "SBER", "60", 10000m, bybit.Id), CancellationToken.None));

        error.Errors.ShouldBe(new Dictionary<string, string> { ["exchangeAccountId"] = "The key of the account does not trade moex." });
    }

    [Test]
    public async Task TInvest_account_on_a_moex_bot_fits()
    {
        var tinvest = new ExchangeAccount(Guid.NewGuid(), "Брокерский", Exchange.TInvest, null, null, "2000000001", new HashSet<MarketCategory> { MarketCategory.Moex }, Now);
        _accounts.GetAsync(tinvest.Id, Arg.Any<CancellationToken>()).Returns(tinvest);

        await _handler.Handle(new CreateBotCommand("SBER EMA", _ema.Id, null, "moex", "SBER", "60", 10000m, tinvest.Id), CancellationToken.None);

        await _repository.Received(1).SaveAsync(Arg.Any<StrategyLibraryChange>(), 0, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Bot_on_a_share_of_moex_is_saved_after_tinvest_confirms_the_ticker()
    {
        await _handler.Handle(new CreateBotCommand("SBER EMA", _ema.Id, null, "moex", "SBER", "60", 10000m, null), CancellationToken.None);

        await _repository.Received(1).SaveAsync(Arg.Any<StrategyLibraryChange>(), 0, Arg.Any<CancellationToken>());
        _bybit.ReceivedCalls().ShouldBeEmpty();
    }

    [Test]
    public async Task Ticker_tinvest_does_not_list_is_an_error_of_the_symbol()
    {
        ValidationFailedException error = await Should.ThrowAsync<ValidationFailedException>(() =>
            _handler.Handle(new CreateBotCommand("NOPE EMA", _ema.Id, null, "moex", "NOPE", "60", 10000m, null), CancellationToken.None));

        error.Errors.ShouldBe(new Dictionary<string, string> { ["symbol"] = "T-Invest has no NOPE in moex." });
        await _repository.DidNotReceiveWithAnyArgs().SaveAsync(default!, default, default);
    }
}