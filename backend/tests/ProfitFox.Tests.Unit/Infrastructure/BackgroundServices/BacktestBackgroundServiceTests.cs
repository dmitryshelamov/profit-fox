using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using ProfitFox.Application.Backtests.Commands.RunNextBacktestWork;
using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Workers;
using ProfitFox.Infrastructure.BackgroundServices;

using Shouldly;

namespace ProfitFox.Tests.Unit.Infrastructure.BackgroundServices;

/// <summary>Requirement "Лог Worker'а": a failure of one piece of work is logged and the lane goes on.</summary>
public class BacktestBackgroundServiceTests
{
    private readonly WorkerSettings _settings = new WorkerSettings();
    private FakeTimeProvider _time = null!;
    private ISender _sender = null!;
    private ServiceProvider _services = null!;
    private ILogger _log = null!;

    [SetUp]
    public void SetUp()
    {
        _time = new FakeTimeProvider();
        _sender = Substitute.For<ISender>();
        _services = new ServiceCollection().AddSingleton(_sender).BuildServiceProvider();
        _log = Substitute.For<ILogger>();
    }

    [TearDown]
    public async Task TearDown()
    {
        await _services.DisposeAsync();
    }

    [Test]
    public async Task Failed_work_on_a_backtest_is_logged_with_the_backtest_and_the_lane_goes_on()
    {
        var backtest = Guid.NewGuid();
        var cause = new InvalidOperationException("database is down");
        TaskCompletionSource nextRan = FailOnceThenWait(new BacktestWorkException(backtest, cause));
        using BacktestBackgroundService service = Service();

        await service.StartAsync(CancellationToken.None);
        await AdvanceUntilAsync(nextRan.Task);
        await service.StopAsync(CancellationToken.None);

        (LogLevel Level, Exception? Exception, IReadOnlyList<KeyValuePair<string, object?>> State) logged = Logged().ShouldHaveSingleItem();
        (logged.Level, logged.Exception).ShouldBe((LogLevel.Error, cause));
        logged.State.ShouldContain(new KeyValuePair<string, object?>("BacktestId", backtest));
        service.ExecuteTask!.IsFaulted.ShouldBeFalse();
    }

    [Test]
    public async Task Failure_outside_any_backtest_is_logged_and_the_lane_goes_on()
    {
        var cause = new InvalidOperationException("database is down");
        TaskCompletionSource nextRan = FailOnceThenWait(cause);
        using BacktestBackgroundService service = Service();

        await service.StartAsync(CancellationToken.None);
        await AdvanceUntilAsync(nextRan.Task);
        await service.StopAsync(CancellationToken.None);

        (LogLevel Level, Exception? Exception, IReadOnlyList<KeyValuePair<string, object?>> _) logged = Logged().ShouldHaveSingleItem();
        (logged.Level, logged.Exception).ShouldBe((LogLevel.Error, cause));
    }

    private TaskCompletionSource FailOnceThenWait(Exception failure)
    {
        var nextRan = new TaskCompletionSource();
        _sender.Send(Arg.Any<RunNextBacktestWorkCommand>(), Arg.Any<CancellationToken>()).Returns(
            _ => throw failure,
            _ =>
            {
                nextRan.TrySetResult();

                return Task.FromResult(false);
            });

        return nextRan;
    }

    private List<(LogLevel Level, Exception? Exception, IReadOnlyList<KeyValuePair<string, object?>> State)> Logged()
    {
        return
        [
            .. _log.ReceivedCalls()
                .Where(call => call.GetMethodInfo().Name == nameof(ILogger.Log))
                .Select(call => ((LogLevel)call.GetArguments()[0]!, (Exception?)call.GetArguments()[3], (IReadOnlyList<KeyValuePair<string, object?>>)call.GetArguments()[2]!))
        ];
    }

    /// <summary>Moves the clock a poll interval at a time until the task completes: the lane may not wait yet when the test moves it.</summary>
    private async Task AdvanceUntilAsync(Task done)
    {
        var timeout = Task.Delay(TimeSpan.FromSeconds(5));

        while (!done.IsCompleted && !timeout.IsCompleted)
        {
            _time.Advance(_settings.PollInterval);
            await Task.Delay(10);
        }

        await done.WaitAsync(TimeSpan.Zero);
    }

    private BacktestBackgroundService Service()
    {
        // NSubstitute cannot proxy ILogger<T> of an internal type, so the logger writes to a substitute of the plain ILogger.
        ILoggerFactory loggers = Substitute.For<ILoggerFactory>();
        loggers.CreateLogger(Arg.Any<string>()).Returns(_log);
        _log.IsEnabled(Arg.Any<LogLevel>()).Returns(true);

        return new BacktestBackgroundService(
            _services.GetRequiredService<IServiceScopeFactory>(),
            _settings,
            _time,
            new Logger<BacktestBackgroundService>(loggers));
    }
}