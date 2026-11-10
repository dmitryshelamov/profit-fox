using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using NSubstitute;
using NSubstitute.ExceptionExtensions;

using ProfitFox.Application.Ports.Market;
using ProfitFox.Application.TradeFlow;
using ProfitFox.Application.Workers;
using ProfitFox.Domain.Entities.Market;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.BackgroundServices;

using Shouldly;

namespace ProfitFox.Tests.Unit.Infrastructure.BackgroundServices;

/// <summary>Decision 2 of collect-trade-flow: the lane of the lease of the trade flow.</summary>
public class TradeFlowBackgroundServiceTests
{
    private readonly TradeFlowSettings _settings = new TradeFlowSettings();
    private FakeTimeProvider _time = null!;
    private ITradeFlowStateRepository _state = null!;
    private IMarketStreams _streams = null!;
    private ITradeFlowMinuteRepository _minutes = null!;
    private IAsyncDisposable _subscription = null!;
    private ITradeFlowJournal _journal = null!;
    private ServiceProvider _services = null!;

    [SetUp]
    public void SetUp()
    {
        _time = new FakeTimeProvider();
        _state = Substitute.For<ITradeFlowStateRepository>();
        _streams = Substitute.For<IMarketStreams>();
        _subscription = Substitute.For<IAsyncDisposable>();
        _streams.SubscribeTradesAsync(
                Arg.Any<IReadOnlyList<Instrument>>(), Arg.Any<Action<Instrument, TradeEvent>>(), Arg.Any<Action<DateTimeOffset>>(), Arg.Any<Action<DateTimeOffset>>(), Arg.Any<CancellationToken>())
            .Returns(_subscription);
        _journal = Substitute.For<ITradeFlowJournal>();
        IScannerPairRepository pairs = Substitute.For<IScannerPairRepository>();
        pairs.GetAllAsync(Arg.Any<CancellationToken>()).Returns([new ScannerPair(new Instrument(MarketCategory.Linear, "SOLUSDT"), true)]);
        _minutes = Substitute.For<ITradeFlowMinuteRepository>();
        ITradeFlowDayRepository days = Substitute.For<ITradeFlowDayRepository>();
        days.GetAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns([]);
        _services = new ServiceCollection()
            .AddSingleton<TimeProvider>(_time)
            .AddSingleton(_settings)
            .AddSingleton(new WorkerIdentity(Guid.NewGuid()))
            .AddSingleton(pairs)
            .AddSingleton(_minutes)
            .AddSingleton(days)
            .AddSingleton(Substitute.For<IStreamBreakRepository>())
            .AddSingleton(Substitute.For<ITradeArchive>())
            .AddSingleton(_streams)
            .AddSingleton(_journal)
            .AddMetrics()
            .AddSingleton<TradeFlowMetrics>()
            .AddScoped<TradeFlowRebuild>()
            .AddScoped<TradeFlowSession>()
            .BuildServiceProvider();
    }

    [TearDown]
    public async Task TearDown()
    {
        await _subscription.DisposeAsync();
        await _services.DisposeAsync();
    }

    [Test]
    public async Task Failed_claim_is_logged_and_tried_again()
    {
        _state.ClaimAsync(default, default, default, default).ReturnsForAnyArgs(Task.FromException<bool>(new InvalidOperationException("database down")), Task.FromResult(false));
        using TradeFlowBackgroundService service = Service();

        await service.StartAsync(CancellationToken.None);
        await AdvanceUntilAsync(() => Claims() >= 2);
        await service.StopAsync(CancellationToken.None);

        _journal.Received().Failed("lease", Arg.Any<InvalidOperationException>());
    }

    // Decision 2: a holder that could not renew takes the stream off and writes nothing more.
    [Test]
    public async Task Lease_that_is_not_renewed_ends_the_session()
    {
        _state.ClaimAsync(default, default, default, default).ReturnsForAnyArgs(true, false);
        _state.RenewAsync(default, default, default).ReturnsForAnyArgs(false);
        using TradeFlowBackgroundService service = Service();

        await service.StartAsync(CancellationToken.None);
        await AdvanceUntilAsync(() => _journal.ReceivedCalls().Any(call => call.GetMethodInfo().Name == nameof(ITradeFlowJournal.Lost)));
        await service.StopAsync(CancellationToken.None);

        await _subscription.Received().DisposeAsync();
        _journal.Received(1).Took();
    }

    // Storage refused a write of the session for the lease: another collector holds the flow.
    [Test]
    public async Task Session_refused_for_the_lease_is_told_lost()
    {
        _state.ClaimAsync(default, default, default, default).ReturnsForAnyArgs(true, false);
        _state.RenewAsync(default, default, default).ReturnsForAnyArgs(true);
        _minutes.LastAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new TradeFlowLeaseLostException());
        using TradeFlowBackgroundService service = Service();

        await service.StartAsync(CancellationToken.None);
        await AdvanceUntilAsync(() => _journal.ReceivedCalls().Any(call => call.GetMethodInfo().Name == nameof(ITradeFlowJournal.Lost)));
        await service.StopAsync(CancellationToken.None);

        _journal.DidNotReceiveWithAnyArgs().Failed(null!, null!);
    }

    [Test]
    public async Task Failed_session_is_logged_and_the_lease_is_tried_again()
    {
        _state.ClaimAsync(default, default, default, default).ReturnsForAnyArgs(true, false);
        _streams.SubscribeTradesAsync(
                Arg.Any<IReadOnlyList<Instrument>>(), Arg.Any<Action<Instrument, TradeEvent>>(), Arg.Any<Action<DateTimeOffset>>(), Arg.Any<Action<DateTimeOffset>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("the stream breaks"));
        using TradeFlowBackgroundService service = Service();

        await service.StartAsync(CancellationToken.None);
        await AdvanceUntilAsync(() => Claims() >= 2);
        await service.StopAsync(CancellationToken.None);

        _journal.Received().Failed("session", Arg.Any<InvalidOperationException>());
    }

    private int Claims()
    {
        return _state.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(ITradeFlowStateRepository.ClaimAsync));
    }

    private TradeFlowBackgroundService Service()
    {
        return new TradeFlowBackgroundService(
            _services.GetRequiredService<IServiceScopeFactory>(),
            _state,
            _settings,
            _services.GetRequiredService<WorkerIdentity>(),
            _journal,
            _time);
    }

    private async Task AdvanceUntilAsync(Func<bool> condition)
    {
        DateTime until = DateTime.UtcNow + TimeSpan.FromSeconds(10);

        while (!condition())
        {
            DateTime.UtcNow.ShouldBeLessThan(until, "The condition did not hold in time.");
            _time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(10);
        }
    }
}