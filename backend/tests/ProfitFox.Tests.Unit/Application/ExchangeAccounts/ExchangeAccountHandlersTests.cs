using System.Security.Cryptography;

using NSubstitute;
using NSubstitute.ExceptionExtensions;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.ExchangeAccounts.Commands.DeleteExchangeAccount;
using ProfitFox.Application.ExchangeAccounts.Commands.RenameExchangeAccount;
using ProfitFox.Application.ExchangeAccounts.Queries.GetExchangeAccount;
using ProfitFox.Application.ExchangeAccounts.Queries.GetExchangeAccountBalance;
using ProfitFox.Application.Ports.ExchangeAccounts;
using ProfitFox.Application.Ports.Security;
using ProfitFox.Domain.Entities.Bots;
using ProfitFox.Domain.Entities.ExchangeAccounts;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.ExchangeAccounts;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Strategies;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.ExchangeAccounts;

public class ExchangeAccountHandlersTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2024-03-10T12:00:00Z");
    private static readonly EncryptedSecret Encrypted = new EncryptedSecret([1], [2], [3]);

    private static readonly ExchangeAccount Account = new ExchangeAccount(
        Guid.Parse("00000000-0000-0000-0000-0000000000a1"),
        "Основной",
        "api-key",
        100,
        new HashSet<MarketCategory> { MarketCategory.Spot },
        Now);

    private IExchangeAccountRepository _accounts = null!;
    private IStrategyLibraryRepository _library = null!;

    [SetUp]
    public void SetUp()
    {
        _accounts = Substitute.For<IExchangeAccountRepository>();
        _accounts.GetAsync(Account.Id, Arg.Any<CancellationToken>()).Returns(Account);
        _accounts.GetSecretAsync(Account.Id, Arg.Any<CancellationToken>()).Returns(Encrypted);
        _accounts.RenameAsync(Account.Id, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        _accounts.DeleteAsync(Account.Id, Arg.Any<CancellationToken>()).Returns(true);
        _library = Substitute.For<IStrategyLibraryRepository>();
        _library.GetBotsAsync(Arg.Any<CancellationToken>()).Returns([Bot("BTC EMA час", Account.Id), Bot("Без аккаунта", null)]);
    }

    // Scenario "Чтение сохранённого аккаунта".
    [Test]
    public async Task Account_is_read_with_the_bots_that_trade_with_it()
    {
        GetExchangeAccountResult? result = await new GetExchangeAccountQueryHandler(_accounts, _library).Handle(new GetExchangeAccountQuery(Account.Id), CancellationToken.None);

        result.ShouldNotBeNull();
        result.Account.ShouldBe(Account);
        result.Bots.Select(bot => bot.Name).ShouldBe(["BTC EMA час"]);
    }

    // Scenario "Неизвестный аккаунт".
    [Test]
    public async Task Unknown_account_is_not_found()
    {
        (await new GetExchangeAccountQueryHandler(_accounts, _library).Handle(new GetExchangeAccountQuery(Guid.NewGuid()), CancellationToken.None)).ShouldBeNull();
    }

    // Scenario "Новое имя".
    [Test]
    public async Task Account_is_renamed()
    {
        await new RenameExchangeAccountCommandHandler(_accounts).Handle(new RenameExchangeAccountCommand(Account.Id, "Testnet"), CancellationToken.None);

        await _accounts.Received(1).RenameAsync(Account.Id, "Testnet", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Empty_name_is_rejected()
    {
        ValidationFailedException exception = await Should.ThrowAsync<ValidationFailedException>(() =>
            new RenameExchangeAccountCommandHandler(_accounts).Handle(new RenameExchangeAccountCommand(Account.Id, " "), CancellationToken.None));

        exception.Errors.Keys.ShouldBe(["name"]);
        await _accounts.DidNotReceiveWithAnyArgs().RenameAsync(default, default!, default);
    }

    [Test]
    public async Task Renaming_an_unknown_account_is_not_found()
    {
        await Should.ThrowAsync<NotFoundException>(() =>
            new RenameExchangeAccountCommandHandler(_accounts).Handle(new RenameExchangeAccountCommand(Guid.NewGuid(), "Other"), CancellationToken.None));
    }

    // Scenario "Аккаунт без ботов".
    [Test]
    public async Task Account_without_bots_is_deleted()
    {
        _library.GetBotsAsync(Arg.Any<CancellationToken>()).Returns([Bot("Без аккаунта", null)]);

        await new DeleteExchangeAccountCommandHandler(_accounts, _library).Handle(new DeleteExchangeAccountCommand(Account.Id), CancellationToken.None);

        await _accounts.Received(1).DeleteAsync(Account.Id, Arg.Any<CancellationToken>());
    }

    // Scenario "Аккаунт с ботом".
    [Test]
    public async Task Account_with_bots_is_a_conflict_naming_them()
    {
        ExchangeAccountInUseConflictException exception = await Should.ThrowAsync<ExchangeAccountInUseConflictException>(() =>
            new DeleteExchangeAccountCommandHandler(_accounts, _library).Handle(new DeleteExchangeAccountCommand(Account.Id), CancellationToken.None));

        exception.BotNames.ShouldBe(["BTC EMA час"]);
        await _accounts.DidNotReceiveWithAnyArgs().DeleteAsync(default, default);
    }

    // A bot saved with the account between the check and the deletion.
    [Test]
    public async Task Account_a_bot_took_meanwhile_is_a_conflict()
    {
        _library.GetBotsAsync(Arg.Any<CancellationToken>()).Returns([]);
        _accounts.DeleteAsync(Account.Id, Arg.Any<CancellationToken>()).ThrowsAsync(new ExchangeAccountInUseException());

        await Should.ThrowAsync<ExchangeAccountInUseConflictException>(() =>
            new DeleteExchangeAccountCommandHandler(_accounts, _library).Handle(new DeleteExchangeAccountCommand(Account.Id), CancellationToken.None));
    }

    [Test]
    public async Task Deleting_an_unknown_account_is_not_found()
    {
        await Should.ThrowAsync<NotFoundException>(() =>
            new DeleteExchangeAccountCommandHandler(_accounts, _library).Handle(new DeleteExchangeAccountCommand(Guid.NewGuid()), CancellationToken.None));
    }

    // Scenario "Баланс аккаунта": the secret is decrypted only to call the exchange.
    [Test]
    public async Task Balance_is_read_with_the_decrypted_secret()
    {
        var balance = new AccountBalance(1675.31m, [new CoinBalance("USDT", 1000m, 1000m)]);
        (ISecretProtector protector, IExchangeKeyInspector inspector) = Exchange();
        inspector.GetBalanceAsync("api-key", "api-secret", Arg.Any<CancellationToken>()).Returns(balance);

        AccountBalance? read = await new GetExchangeAccountBalanceQueryHandler(_accounts, inspector, protector)
            .Handle(new GetExchangeAccountBalanceQuery(Account.Id), CancellationToken.None);

        read.ShouldBe(balance);
    }

    // Scenario "Ключ отозван".
    [Test]
    public async Task Balance_of_a_key_bybit_no_longer_accepts_is_a_conflict()
    {
        (ISecretProtector protector, IExchangeKeyInspector inspector) = Exchange();
        inspector.GetBalanceAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ExchangeKeyRejectedException("Bybit does not accept the key: API key is invalid."));

        ConflictException exception = await Should.ThrowAsync<ConflictException>(() =>
            new GetExchangeAccountBalanceQueryHandler(_accounts, inspector, protector).Handle(new GetExchangeAccountBalanceQuery(Account.Id), CancellationToken.None));

        exception.Message.ShouldBe("Bybit does not accept the key: API key is invalid.");
    }

    // A master key other than the one the secret was encrypted with: the account has to be added again.
    [Test]
    public async Task Balance_of_a_secret_the_master_key_does_not_decrypt_is_a_conflict()
    {
        (ISecretProtector protector, IExchangeKeyInspector inspector) = Exchange();
        protector.Decrypt(Account.Id, Encrypted).Throws(new CryptographicException("The computed authentication tag did not match the input authentication tag."));

        ConflictException exception = await Should.ThrowAsync<ConflictException>(() =>
            new GetExchangeAccountBalanceQueryHandler(_accounts, inspector, protector).Handle(new GetExchangeAccountBalanceQuery(Account.Id), CancellationToken.None));

        exception.Message.ShouldBe("The secret of the account does not decrypt with the current master key; delete the account and add it again.");
        await inspector.DidNotReceiveWithAnyArgs().GetBalanceAsync(default!, default!, default);
    }

    // The account was deleted between reading it and reading its secret.
    [Test]
    public async Task Balance_of_an_account_whose_secret_is_gone_is_not_found()
    {
        (ISecretProtector protector, IExchangeKeyInspector inspector) = Exchange();
        _accounts.GetSecretAsync(Account.Id, Arg.Any<CancellationToken>()).Returns((EncryptedSecret?)null);

        (await new GetExchangeAccountBalanceQueryHandler(_accounts, inspector, protector).Handle(new GetExchangeAccountBalanceQuery(Account.Id), CancellationToken.None))
            .ShouldBeNull();
        await inspector.DidNotReceiveWithAnyArgs().GetBalanceAsync(default!, default!, default);
    }

    [Test]
    public async Task Balance_of_an_unknown_account_is_not_found()
    {
        (ISecretProtector protector, IExchangeKeyInspector inspector) = Exchange();

        (await new GetExchangeAccountBalanceQueryHandler(_accounts, inspector, protector).Handle(new GetExchangeAccountBalanceQuery(Guid.NewGuid()), CancellationToken.None))
            .ShouldBeNull();
        await inspector.DidNotReceiveWithAnyArgs().GetBalanceAsync(default!, default!, default);
    }

    private static (ISecretProtector Protector, IExchangeKeyInspector Inspector) Exchange()
    {
        ISecretProtector protector = Substitute.For<ISecretProtector>();
        protector.Decrypt(Account.Id, Encrypted).Returns("api-secret");

        return (protector, Substitute.For<IExchangeKeyInspector>());
    }

    private static Bot Bot(string name, Guid? accountId)
    {
        return new Bot(Guid.NewGuid(), name, Guid.NewGuid(), new Dictionary<string, NodeValue>(), new Instrument(MarketCategory.Spot, "BTCUSDT"), Interval.OneHour, 1000m, Now, Now, accountId);
    }
}