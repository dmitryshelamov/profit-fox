using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

using NSubstitute;
using NSubstitute.Core;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.IdeaTrades.Runtime;
using ProfitFox.Application.LiveTrading;
using ProfitFox.Application.LiveTrading.Commands.ClaimLiveBot;
using ProfitFox.Application.LiveTrading.Commands.RunLiveBot;
using ProfitFox.Application.LiveTrading.Runtime;
using ProfitFox.Application.Workers;
using ProfitFox.Domain.Entities.LiveTrading;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.LiveTrading;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Infrastructure.BackgroundServices;

using Shouldly;

namespace ProfitFox.Tests.Unit.Infrastructure.BackgroundServices;

/// <summary>Requirement "Отпускание аренд при остановке".</summary>
public class LiveTradingBackgroundServiceTests
{
    private readonly WorkerIdentity _worker = new WorkerIdentity(Guid.NewGuid());
    private FakeTimeProvider _time = null!;
    private ISender _sender = null!;
    private ILiveBotRepository _repository = null!;
    private IIdeaTradeRepository _trades = null!;
    private ServiceProvider _services = null!;
    private ILogger _log = null!;
    private LeasedBot _leased = null!;
    private TaskCompletionSource _running = null!;

    [SetUp]
    public void SetUp()
    {
        _time = new FakeTimeProvider();
        _sender = Substitute.For<ISender>();
        _repository = Substitute.For<ILiveBotRepository>();
        _repository.GetChangedAsync(default, default, default).ReturnsForAnyArgs([]);
        _trades = Substitute.For<IIdeaTradeRepository>();
        _trades.GetChangedAsync(default, default, default).ReturnsForAnyArgs([]);
        _services = new ServiceCollection().AddSingleton(_sender).AddSingleton(_repository).AddSingleton(_trades).BuildServiceProvider();
        _log = Substitute.For<ILogger>();
        _log.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        _leased = new LeasedBot(LiveBot.New(Guid.NewGuid()).Start(1, new Dictionary<string, NodeValue>()), 4);
        _running = new TaskCompletionSource();
        _sender.Send(Arg.Any<ClaimLiveBotCommand>(), Arg.Any<CancellationToken>()).Returns(_leased, (LeasedBot?)null);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _services.DisposeAsync();
    }

    // The SDK answers a call cut by the stop with a failure, not a cancellation: the bots are given up all the same.
    [Test]
    public async Task Bots_are_given_up_on_stop_even_when_a_session_fails_as_it_stops()
    {
        _sender.Send(Arg.Any<RunLiveBotCommand>(), Arg.Any<CancellationToken>()).Returns(call => RunUntilStopAsync(call.Arg<CancellationToken>(), failOnStop: true));

        await RunAndStopAsync();

        (Guid BotId, long Epoch)[] given = [(_leased.Bot.BotId, 4L)];
        await _repository.Received(1).ReleaseAllAsync(
            _worker.Id,
            Arg.Is<IReadOnlyCollection<(Guid BotId, long Epoch)>>(bots => bots!.SequenceEqual(given)),
            CancellationToken.None);
    }

    // The stop may come while the lane takes the next bot, and the session it ends may end before the lane looks at its sessions.
    [Test]
    public async Task Bot_is_given_up_when_its_session_ends_with_a_stop_that_comes_while_the_next_bot_is_taken()
    {
        _sender.Send(Arg.Any<RunLiveBotCommand>(), Arg.Any<CancellationToken>()).Returns(call => RunUntilStopAsync(call.Arg<CancellationToken>(), failOnStop: true));
        using LiveTradingBackgroundService service = Service();
        var stopping = new TaskCompletionSource<Task>();
        _sender.Send(Arg.Any<ClaimLiveBotCommand>(), Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult<LeasedBot?>(_leased), _ => StopAsTheNextBotIsTakenAsync(service, stopping));

        await service.StartAsync(CancellationToken.None);
        await await stopping.Task.WaitAsync(TimeSpan.FromSeconds(5));

        (Guid BotId, long Epoch)[] given = [(_leased.Bot.BotId, 4L)];
        await _repository.Received(1).ReleaseAllAsync(
            _worker.Id,
            Arg.Is<IReadOnlyCollection<(Guid BotId, long Epoch)>>(bots => bots!.SequenceEqual(given)),
            CancellationToken.None);
    }

    // A session that ends because the Worker stops is no failure.
    [Test]
    public async Task Session_cancelled_by_the_stop_is_not_logged_as_a_failure()
    {
        _sender.Send(Arg.Any<RunLiveBotCommand>(), Arg.Any<CancellationToken>()).Returns(call => RunUntilStopAsync(call.Arg<CancellationToken>(), failOnStop: false));

        await RunAndStopAsync();

        Failures().ShouldBeEmpty();
    }

    private async Task RunAndStopAsync()
    {
        using LiveTradingBackgroundService service = Service();

        await service.StartAsync(CancellationToken.None);
        await _running.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.StopAsync(CancellationToken.None);
    }

    private LiveTradingBackgroundService Service()
    {
        // NSubstitute cannot proxy ILogger<T> of an internal type, so the logger writes to a substitute of the plain ILogger.
        ILoggerFactory loggers = Substitute.For<ILoggerFactory>();
        loggers.CreateLogger(Arg.Any<string>()).Returns(_log);

        return new LiveTradingBackgroundService(
            _services.GetRequiredService<IServiceScopeFactory>(),
            new LiveBotLeases(_repository, new TradingSettings(), new WorkerSettings(), _worker, _time),
            new IdeaTradeLeases(_trades, new TradingSettings(), new WorkerSettings(), _worker, _time),
            new WorkerSettings(),
            _worker,
            _time,
            new Logger<LiveTradingBackgroundService>(loggers));
    }

    /// <summary>Stops the service once the session of the bot taken before runs, and answers the take when that session has ended.</summary>
    private async Task<LeasedBot?> StopAsTheNextBotIsTakenAsync(LiveTradingBackgroundService service, TaskCompletionSource<Task> stopping)
    {
        await _running.Task.WaitAsync(TimeSpan.FromSeconds(5));
        // The stop waits for the lane, which waits for this take: it is awaited by the test.
        stopping.SetResult(service.StopAsync(CancellationToken.None));
        using var failing = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        while (!Failures().Any())
        {
            await Task.Delay(1, failing.Token);
        }

        // The session ends right after it logs its failure; nothing outside it tells when.
        await Task.Delay(100);

        return null;
    }

    private IEnumerable<ICall> Failures()
    {
        return _log.ReceivedCalls().Where(call => call.GetMethodInfo().Name == nameof(ILogger.Log) && (LogLevel)call.GetArguments()[0]! == LogLevel.Error);
    }

    private async Task RunUntilStopAsync(CancellationToken stoppingToken, bool failOnStop)
    {
        _running.TrySetResult();

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) when (failOnStop)
        {
            throw new ExchangeUnavailableException("Bybit is unavailable: the request was cancelled.");
        }
    }
}