using Microsoft.Extensions.Time.Testing;

using NSubstitute;
using NSubstitute.ExceptionExtensions;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.ExchangeAccounts.Commands.CreateExchangeAccount;
using ProfitFox.Application.Ports.ExchangeAccounts;
using ProfitFox.Application.Ports.Security;
using ProfitFox.Domain.Entities.ExchangeAccounts;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.ExchangeAccounts;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.ExchangeAccounts;

public class CreateExchangeAccountTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2024-03-10T12:00:00Z");
    private static readonly EncryptedSecret Encrypted = new EncryptedSecret([1], [2], [3]);

    private static readonly ExchangeKeyInfo TradingKey = new ExchangeKeyInfo(
        100,
        IsReadOnly: false,
        IsUnifiedAccount: true,
        CanWithdraw: false,
        new HashSet<MarketCategory> { MarketCategory.Spot, MarketCategory.Linear });

    private IExchangeAccountRepository _repository = null!;
    private IExchangeKeyInspector _inspector = null!;
    private ISecretProtector _protector = null!;
    private CreateExchangeAccountCommandHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _repository = Substitute.For<IExchangeAccountRepository>();
        _repository.AddAsync(Arg.Any<ExchangeAccount>(), Arg.Any<EncryptedSecret>(), Arg.Any<CancellationToken>()).Returns(true);
        _inspector = Substitute.For<IExchangeKeyInspector>();
        _inspector.InspectAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(TradingKey);
        _protector = Substitute.For<ISecretProtector>();
        _protector.Encrypt(Arg.Any<Guid>(), Arg.Any<string>()).Returns(Encrypted);
        _handler = new CreateExchangeAccountCommandHandler(_repository, _inspector, _protector, new FakeTimeProvider(Now));
    }

    // Scenario "Валидный аккаунт".
    [Test]
    public async Task Valid_account_is_saved_with_the_encrypted_secret()
    {
        Guid id = await _handler.Handle(Command(), CancellationToken.None);

        await _inspector.Received(1).InspectAsync("api-key", "api-secret", Arg.Any<CancellationToken>());
        _protector.Received(1).Encrypt(id, "api-secret");
        await _repository.Received(1).AddAsync(
            Arg.Is<ExchangeAccount>(account => account!.Id == id
                                               && account.Name == "Основной"
                                               && account.ApiKey == "api-key"
                                               && account.Uid == 100
                                               && account.Categories.SetEquals(new[] { MarketCategory.Spot, MarketCategory.Linear })
                                               && account.CreatedAt == Now),
            Encrypted,
            Arg.Any<CancellationToken>());
    }

    // Scenario "Ключ только для спота".
    [Test]
    public async Task Categories_the_key_trades_in_are_kept()
    {
        _inspector.InspectAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(TradingKey with { TradableCategories = new HashSet<MarketCategory> { MarketCategory.Spot } });

        await _handler.Handle(Command(), CancellationToken.None);

        await _repository.Received(1).AddAsync(
            Arg.Is<ExchangeAccount>(account => account!.Categories.SetEquals(new[] { MarketCategory.Spot })),
            Arg.Any<EncryptedSecret>(),
            Arg.Any<CancellationToken>());
    }

    // Scenario "Пустое имя": every field is checked before Bybit is asked.
    [Test]
    public async Task Malformed_fields_are_rejected_without_asking_bybit()
    {
        ValidationFailedException exception = await Should.ThrowAsync<ValidationFailedException>(() =>
            _handler.Handle(new CreateExchangeAccountCommand(" ", "", ""), CancellationToken.None));

        exception.Errors.Keys.Order().ShouldBe(["apiKey", "apiSecret", "name"]);
        await _inspector.DidNotReceiveWithAnyArgs().InspectAsync(default!, default!, default);
        await _repository.DidNotReceiveWithAnyArgs().AddAsync(default!, default!, default);
    }

    // Scenario "Bybit не принимает ключ".
    [Test]
    public async Task Key_bybit_does_not_accept_is_an_error_of_the_key()
    {
        _inspector.InspectAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ExchangeKeyRejectedException("Bybit does not accept the key: API key is invalid."));

        ValidationFailedException exception = await Should.ThrowAsync<ValidationFailedException>(() => _handler.Handle(Command(), CancellationToken.None));

        exception.Errors.ShouldBe(new Dictionary<string, string> { ["apiKey"] = "Bybit does not accept the key: API key is invalid." });
        await _repository.DidNotReceiveWithAnyArgs().AddAsync(default!, default!, default);
    }

    // Scenarios "Ключ только для чтения" and "Ключ с правом на вывод средств".
    [Test]
    public async Task Key_the_rules_reject_is_an_error_of_the_key()
    {
        _inspector.InspectAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(TradingKey with { IsReadOnly = true, CanWithdraw = true });

        ValidationFailedException exception = await Should.ThrowAsync<ValidationFailedException>(() => _handler.Handle(Command(), CancellationToken.None));

        exception.Errors["apiKey"].ShouldBe(
            "The key is read-only; a bot needs a key that can trade. The key can withdraw funds; create a key without the withdrawal permission.");
        await _repository.DidNotReceiveWithAnyArgs().AddAsync(default!, default!, default);
    }

    // Scenario "Повторный ключ того же аккаунта".
    [Test]
    public async Task Account_stored_already_is_an_error_of_the_key()
    {
        _repository.AddAsync(Arg.Any<ExchangeAccount>(), Arg.Any<EncryptedSecret>(), Arg.Any<CancellationToken>()).Returns(false);

        ValidationFailedException exception = await Should.ThrowAsync<ValidationFailedException>(() => _handler.Handle(Command(), CancellationToken.None));

        exception.Errors.ShouldBe(new Dictionary<string, string> { ["apiKey"] = "Bybit account 100 is already added." });
    }

    // Scenario "Bybit не отвечает при создании".
    [Test]
    public async Task Bybit_unavailable_saves_nothing()
    {
        _inspector.InspectAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ExchangeUnavailableException("Bybit is unavailable."));

        await Should.ThrowAsync<ExchangeUnavailableException>(() => _handler.Handle(Command(), CancellationToken.None));
        await _repository.DidNotReceiveWithAnyArgs().AddAsync(default!, default!, default);
    }

    private static CreateExchangeAccountCommand Command()
    {
        return new CreateExchangeAccountCommand("Основной", "api-key", "api-secret");
    }
}