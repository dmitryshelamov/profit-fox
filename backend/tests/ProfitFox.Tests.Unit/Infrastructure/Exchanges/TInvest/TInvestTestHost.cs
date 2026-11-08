using System.Diagnostics.Metrics;

using Grpc.Core;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using ProfitFox.Application.Ports.Security;
using ProfitFox.Domain.Entities.ExchangeAccounts;
using ProfitFox.Domain.Entities.ExchangeAccounts.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.ExchangeAccounts;
using ProfitFox.Infrastructure.Exchanges.Retry;
using ProfitFox.Infrastructure.Exchanges.TInvest;
using ProfitFox.Tests.Shared.TInvest;

namespace ProfitFox.Tests.Unit.Infrastructure.Exchanges.TInvest;

/// <summary>The adapter of T-Invest over <see cref="MockTInvestApi" />, a fake clock and storage with or without a T-Invest account.</summary>
internal sealed class TInvestTestHost : IDisposable
{
    public const string Token = "t.token";

    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2024-03-10T12:00:00Z");

    private readonly ServiceProvider _services;

    public TInvestTestHost(bool withAccount = true)
    {
        Api = new MockTInvestApi();
        Time = new FakeTimeProvider(Start);
        Settings = new TInvestSettings();
        Accounts = Substitute.For<IExchangeAccountRepository>();
        var account = new ExchangeAccount(Guid.NewGuid(), "Брокерский", Exchange.TInvest, null, null, "2000000001", new HashSet<MarketCategory> { MarketCategory.Moex }, Start.AddDays(-1));
        var encrypted = new EncryptedSecret([1], [2], [3]);
        Accounts.GetAllAsync(Arg.Any<CancellationToken>()).Returns(withAccount ? [account] : []);
        Accounts.GetSecretAsync(account.Id, Arg.Any<CancellationToken>()).Returns(encrypted);
        ISecretProtector protector = Substitute.For<ISecretProtector>();
        protector.Decrypt(account.Id, encrypted).Returns(Token);
        _services = new ServiceCollection().AddMetrics().AddScoped(_ => Accounts).BuildServiceProvider();
        Limiter = new TInvestRequestLimiter(Settings, Time);
        Call = new TInvestCall(new Invokers(Api), Limiter, new TInvestMetrics(_services.GetRequiredService<IMeterFactory>()), new ExchangeResilienceExecutor(Time), Settings, Time);
        Tokens = new TInvestTokenSource(_services.GetRequiredService<IServiceScopeFactory>(), protector, Time);
        Shares = new TInvestShares(Call, Tokens, Time);
        MarketData = new TInvestMarketDataClient(Call, Tokens, Shares);
        AccountClient = new TInvestAccountClient(Call);
    }

    public MockTInvestApi Api { get; }

    public FakeTimeProvider Time { get; }

    public TInvestSettings Settings { get; }

    public IExchangeAccountRepository Accounts { get; }

    public TInvestRequestLimiter Limiter { get; }

    public TInvestCall Call { get; }

    public TInvestTokenSource Tokens { get; }

    public TInvestShares Shares { get; }

    public TInvestMarketDataClient MarketData { get; }

    public TInvestAccountClient AccountClient { get; }

    public void Dispose()
    {
        _services.Dispose();
    }

    private sealed class Invokers(CallInvoker invoker) : ITInvestCallInvokers
    {
        public CallInvoker Invoker => invoker;
    }
}