using NSubstitute;
using NSubstitute.ExceptionExtensions;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.ExchangeAccounts.Queries.ListBrokerAccounts;
using ProfitFox.Application.Ports.ExchangeAccounts;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.ExchangeAccounts;

/// <summary>Requirement "Счета токена T-Invest".</summary>
public class ListBrokerAccountsTests
{
    private static readonly BrokerAccount Brokerage = new BrokerAccount("2000000001", "Брокерский счёт", BrokerAccountType.Brokerage, BrokerAccountStatus.Open, BrokerAccountAccess.Full);
    private static readonly BrokerAccount ClosedIis = new BrokerAccount("2000000002", "ИИС", BrokerAccountType.Iis, BrokerAccountStatus.Closed, BrokerAccountAccess.Full);

    private IBrokerAccountReader _reader = null!;
    private ListBrokerAccountsQueryHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _reader = Substitute.For<IBrokerAccountReader>();
        _reader.ListAccountsAsync("t.token", Arg.Any<CancellationToken>()).Returns([Brokerage, ClosedIis]);
        _handler = new ListBrokerAccountsQueryHandler(_reader);
    }

    // Scenario "Два счёта токена".
    [Test]
    public async Task Accounts_of_the_token_come_with_whether_each_can_be_saved()
    {
        IReadOnlyList<BrokerAccountOption> accounts = await _handler.Handle(new ListBrokerAccountsQuery("t.token"), CancellationToken.None);

        accounts.ShouldBe([new BrokerAccountOption(Brokerage, null), new BrokerAccountOption(ClosedIis, "not_open")]);
    }

    // Scenario "Токен отклонён".
    [Test]
    public async Task Token_tinvest_does_not_accept_is_an_error_of_the_token()
    {
        _reader.ListAccountsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).ThrowsAsync(new ExchangeKeyRejectedException("T-Invest does not accept the token."));

        ValidationFailedException exception = await Should.ThrowAsync<ValidationFailedException>(() => _handler.Handle(new ListBrokerAccountsQuery("t.token"), CancellationToken.None));

        exception.Errors.ShouldBe(new Dictionary<string, string> { ["token"] = "T-Invest does not accept the token." });
    }

    [Test]
    public async Task Empty_token_is_an_error_of_the_token_without_a_call()
    {
        ValidationFailedException exception = await Should.ThrowAsync<ValidationFailedException>(() => _handler.Handle(new ListBrokerAccountsQuery(" "), CancellationToken.None));

        exception.Errors.Keys.ShouldBe(["token"]);
        await _reader.DidNotReceiveWithAnyArgs().ListAccountsAsync(default!, default);
    }

    [Test]
    public async Task TInvest_unavailable_propagates()
    {
        _reader.ListAccountsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).ThrowsAsync(new ExchangeUnavailableException("T-Invest is unavailable."));

        await Should.ThrowAsync<ExchangeUnavailableException>(() => _handler.Handle(new ListBrokerAccountsQuery("t.token"), CancellationToken.None));
    }
}