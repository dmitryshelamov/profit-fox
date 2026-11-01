using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using ProfitFox.Application.MarketData.Commands.RunNextCandleImport;
using ProfitFox.Application.Workers;
using ProfitFox.Infrastructure.BackgroundServices;

using Shouldly;

namespace ProfitFox.Tests.Unit.Infrastructure.BackgroundServices;

public class CandleImportBackgroundServiceTests
{
    private readonly WorkerSettings _settings = new WorkerSettings();
    private FakeTimeProvider _time = null!;
    private ISender _sender = null!;
    private ServiceProvider _services = null!;

    [SetUp]
    public void SetUp()
    {
        _time = new FakeTimeProvider();
        _sender = Substitute.For<ISender>();
        _services = new ServiceCollection().AddSingleton(_sender).BuildServiceProvider();
    }

    [TearDown]
    public async Task TearDown()
    {
        await _services.DisposeAsync();
    }

    [Test]
    public async Task Next_job_is_taken_right_after_one_ran()
    {
        var calls = 0;
        var third = new TaskCompletionSource();
        _sender.Send(Arg.Any<RunNextCandleImportCommand>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            if (++calls == 3)
            {
                third.SetResult();
            }

            return calls < 3;
        });
        using CandleImportBackgroundService service = Service();

        await service.StartAsync(CancellationToken.None);

        await third.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.StopAsync(CancellationToken.None);
    }

    [Test]
    public async Task Worker_without_a_job_asks_again_after_the_poll_interval()
    {
        var calls = 0;
        var second = new TaskCompletionSource();
        _sender.Send(Arg.Any<RunNextCandleImportCommand>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            if (++calls == 2)
            {
                second.SetResult();
            }

            return false;
        });
        using CandleImportBackgroundService service = Service();

        await service.StartAsync(CancellationToken.None);
        await Task.Delay(100);
        calls.ShouldBe(1);
        _time.Advance(_settings.PollInterval);

        await second.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.StopAsync(CancellationToken.None);
    }

    [Test]
    public async Task Failed_job_does_not_stop_the_lane()
    {
        var nextRan = new TaskCompletionSource();
        _sender.Send(Arg.Any<RunNextCandleImportCommand>(), Arg.Any<CancellationToken>()).Returns(
            _ => throw new InvalidOperationException("database is down"),
            _ =>
            {
                nextRan.SetResult();

                return Task.FromResult(false);
            });
        using CandleImportBackgroundService service = Service();

        await service.StartAsync(CancellationToken.None);

        await AdvanceUntilAsync(nextRan.Task);
        service.ExecuteTask!.IsFaulted.ShouldBeFalse();
        await service.StopAsync(CancellationToken.None);
    }

    [Test]
    public async Task Shutdown_stops_the_lane_while_it_waits()
    {
        var asked = new TaskCompletionSource();
        _sender.Send(Arg.Any<RunNextCandleImportCommand>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            asked.TrySetResult();

            return false;
        });
        using CandleImportBackgroundService service = Service();
        await service.StartAsync(CancellationToken.None);
        await asked.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await service.StopAsync(CancellationToken.None);

        service.ExecuteTask!.Status.ShouldBe(TaskStatus.RanToCompletion);
    }

    // The job stays Running and its lease expires for any Worker, so a shutdown is no failure of the job.
    [Test]
    public async Task Job_cancelled_by_shutdown_is_not_logged_as_a_failure()
    {
        var jobStarted = new TaskCompletionSource();
        _sender.Send(Arg.Any<RunNextCandleImportCommand>(), Arg.Any<CancellationToken>()).Returns(async call =>
        {
            jobStarted.SetResult();
            await Task.Delay(Timeout.Infinite, call.Arg<CancellationToken>());

            return true;
        });
        // NSubstitute cannot proxy ILogger<T> of an internal type, so the logger writes to a substitute of the plain ILogger.
        ILogger log = Substitute.For<ILogger>();
        ILoggerFactory loggers = Substitute.For<ILoggerFactory>();
        loggers.CreateLogger(Arg.Any<string>()).Returns(log);
        using CandleImportBackgroundService service = Service(new Logger<CandleImportBackgroundService>(loggers));

        await service.StartAsync(CancellationToken.None);
        await jobStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.StopAsync(CancellationToken.None);

        log.ReceivedCalls().Where(call => call.GetMethodInfo().Name == nameof(ILogger.Log)).ShouldBeEmpty();
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

    private CandleImportBackgroundService Service(ILogger<CandleImportBackgroundService>? logger = null)
    {
        return new CandleImportBackgroundService(
            _services.GetRequiredService<IServiceScopeFactory>(),
            _settings,
            _time,
            logger ?? NullLogger<CandleImportBackgroundService>.Instance);
    }
}