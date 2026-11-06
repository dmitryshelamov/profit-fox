using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using NSubstitute;
using NSubstitute.ExceptionExtensions;

using ProfitFox.Application.Backtests;
using ProfitFox.Application.Ports.Market;
using ProfitFox.Application.Scanner;
using ProfitFox.Application.Scanner.Backfill;
using ProfitFox.Application.Scanner.Ideas;
using ProfitFox.Application.Scanner.Minute;
using ProfitFox.Application.Scanner.Universe;
using ProfitFox.Application.Workers;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.BackgroundServices;

using Shouldly;

namespace ProfitFox.Tests.Unit.Infrastructure.BackgroundServices;

/// <summary>Decision 2: the lane of the lease of the stream.</summary>
public class ScannerBackgroundServiceTests
{
    private readonly ScannerSettings _settings = new ScannerSettings();
    private FakeTimeProvider _time = null!;
    private IScannerStateRepository _state = null!;
    private IMarketStreams _streams = null!;
    private IAsyncDisposable _subscription = null!;
    private IScannerJournal _journal = null!;
    private ServiceProvider _services = null!;

    [SetUp]
    public void SetUp()
    {
        _time = new FakeTimeProvider();
        _state = Substitute.For<IScannerStateRepository>();
        _streams = Substitute.For<IMarketStreams>();
        _subscription = Substitute.For<IAsyncDisposable>();
        _streams.SubscribeMinuteCandlesAsync(Arg.Any<IReadOnlyList<Instrument>>(), Arg.Any<Action<Instrument, Candle>>(), Arg.Any<Action>(), Arg.Any<CancellationToken>())
            .Returns(_subscription);
        _journal = Substitute.For<IScannerJournal>();
        IMarketDataFeed feed = Substitute.For<IMarketDataFeed>();
        feed.ListInstrumentsAsync(Arg.Any<MarketCategory>(), Arg.Any<CancellationToken>()).Returns([]);
        feed.GetTickersAsync(Arg.Any<MarketCategory>(), Arg.Any<CancellationToken>()).Returns([]);
        IScannerPairRepository pairs = Substitute.For<IScannerPairRepository>();
        pairs.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);
        IMarketSnapshotRepository snapshots = Substitute.For<IMarketSnapshotRepository>();
        snapshots.ReplaceAsync(default, default!, default, default).ReturnsForAnyArgs(true);
        _services = new ServiceCollection()
            .AddSingleton<TimeProvider>(_time)
            .AddSingleton(_settings)
            .AddSingleton(new WorkerIdentity(Guid.NewGuid()))
            .AddSingleton(feed)
            .AddSingleton(pairs)
            .AddSingleton(snapshots)
            .AddSingleton(_state)
            .AddSingleton(_streams)
            .AddSingleton(_journal)
            .AddSingleton(Substitute.For<ICandleRepository>())
            .AddSingleton(Substitute.For<IOpenInterestRepository>())
            .AddSingleton(Substitute.For<IFundingRateRepository>())
            .AddScoped<ScannerMarket>()
            .AddScoped<ScannerUniverse>()
            .AddScoped<ScannerBackfill>()
            .AddSingleton(Substitute.For<IIdeaRepository>())
            .AddSingleton(Substitute.For<IIdeaSignalRepository>())
            .AddSingleton(Substitute.For<IIdeaExitRuleRepository>())
            .AddSingleton(Substitute.For<IIdeaReplayRepository>())
            .AddSingleton(Substitute.For<IIdeaVolatilityRepository>())
            .AddSingleton(Substitute.For<IIdeaBucketRepository>())
            .AddSingleton(Substitute.For<IIdeaCalibrationRepository>())
            .AddSingleton(Substitute.For<IIdeaEngineVersionRepository>())
            .AddSingleton(new IdeasSettings())
            .AddSingleton(new BacktestSettings())
            .AddSingleton(new IdeasEngineVersion(IdeasEngineVersion.Current))
            .AddScoped<ScannerPace>()
            .AddScoped<LiveIdeas>()
            .AddScoped<IdeaStatistics>()
            .AddScoped<IdeaRecomputes>()
            .AddScoped<IdeasReplay>()
            .AddScoped<ScannerMinuteCycle>()
            .AddScoped<ScannerSession>()
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
        using ScannerBackgroundService service = Service();

        await service.StartAsync(CancellationToken.None);
        await AdvanceUntilAsync(() => _state.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(IScannerStateRepository.ClaimAsync)) >= 2);
        await service.StopAsync(CancellationToken.None);

        _journal.Received().Failed("lease", Arg.Any<InvalidOperationException>());
    }

    // Decision 2: a holder that could not renew takes the stream off and writes nothing more.
    [Test]
    public async Task Lease_that_is_not_renewed_ends_the_session()
    {
        _state.ClaimAsync(default, default, default, default).ReturnsForAnyArgs(true, false);
        _state.RenewAsync(default, default, default).ReturnsForAnyArgs(false);
        using ScannerBackgroundService service = Service();

        await service.StartAsync(CancellationToken.None);
        await AdvanceUntilAsync(() => _journal.ReceivedCalls().Any(call => call.GetMethodInfo().Name == nameof(IScannerJournal.Lost)));
        await service.StopAsync(CancellationToken.None);

        await _subscription.Received().DisposeAsync();
        _journal.Received(1).Took();
    }

    [Test]
    public async Task Failed_session_is_logged_and_the_lease_is_tried_again()
    {
        _state.ClaimAsync(default, default, default, default).ReturnsForAnyArgs(true, false);
        _streams.SubscribeMinuteCandlesAsync(Arg.Any<IReadOnlyList<Instrument>>(), Arg.Any<Action<Instrument, Candle>>(), Arg.Any<Action>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("the stream breaks"));
        using ScannerBackgroundService service = Service();

        await service.StartAsync(CancellationToken.None);
        await AdvanceUntilAsync(() => _state.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(IScannerStateRepository.ClaimAsync)) >= 2);
        await service.StopAsync(CancellationToken.None);

        _journal.Received().Failed("session", Arg.Any<InvalidOperationException>());
    }

    private ScannerBackgroundService Service()
    {
        return new ScannerBackgroundService(
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