using ProfitFox.Application.Commands.Accounts.DeleteAccount;
using ProfitFox.Application.Exceptions;
using ProfitFox.Domain.Entities.Accounts;
using ProfitFox.Domain.ValueObjects.Accounts;

namespace ProfitFox.Tests.Unit.Application.Commands.Accounts;

/// <summary>
///     Change take-idea-trades, task 6.3: an account with a trade that holds its pair is not removed (spec idea-trades
///     «Аккаунт со сделкой не удаляется»).
/// </summary>
public sealed class DeleteAccountCommandHandlerTests
{
    private readonly MemoryExchangeAccountRepository _accounts = new MemoryExchangeAccountRepository();
    private readonly ExchangeAccount _main = new ExchangeAccount(Guid.NewGuid(), "Основной", 1, "key", DateTimeOffset.UnixEpoch);

    [SetUp]
    public async Task Add()
    {
        await _accounts.AddAsync(_main, new ProtectedSecret([1], [2], [3]), CancellationToken.None);
    }

    // Spec «Открытая сделка».
    [Test]
    public void An_account_with_an_open_trade_is_refused_and_stays()
    {
        _accounts.Trading.Add(_main.Id);

        TradeConflictException? refused = Assert.ThrowsAsync<TradeConflictException>(() => new DeleteAccountCommandHandler(_accounts).Handle(new DeleteAccountCommand(_main.Id), CancellationToken.None));

        Assert.That(refused!.Message, Does.StartWith("У аккаунта есть незакрытая сделка"));
        Assert.That(_accounts.FindAsync(_main.Id, CancellationToken.None).Result, Is.Not.Null);
    }

    // Spec «Только закрытые сделки»: the store keeps the name of the account with the trades.
    [Test]
    public async Task An_account_whose_trades_are_all_closed_is_removed()
    {
        Assert.That(await new DeleteAccountCommandHandler(_accounts).Handle(new DeleteAccountCommand(_main.Id), CancellationToken.None), Is.True);
    }
}
