using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using ProfitFox.Application.IdeaAutos.Commands.RunNextAutoEntry;
using ProfitFox.Application.Workers;
using ProfitFox.Infrastructure.BackgroundServices;

using Shouldly;

namespace ProfitFox.Tests.Unit.Infrastructure.BackgroundServices;

/// <summary>The lane of the entries of the autos with money: a request right after a request, a wait when there is none, and a failure that does not stop it.</summary>
public class IdeaAutoEntryBackgroundServiceTests
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
    public async Task Next_request_is_taken_right_after_one_was_made()
    {
        var calls = 0;
        var third = new TaskCompletionSource();
        _sender.Send(Arg.Any<RunNextAutoEntryCommand>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            if (++calls == 3)
            {
                third.SetResult();
            }

            return calls < 3;
        });
        using IdeaAutoEntryBackgroundService service = Service();

        await service.StartAsync(CancellationToken.None);

        await third.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.StopAsync(CancellationToken.None);
    }

    [Test]
    public async Task Lane_without_a_request_asks_again_after_the_poll_interval()
    {
        var calls = 0;
        var second = new TaskCompletionSource();
        _sender.Send(Arg.Any<RunNextAutoEntryCommand>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            if (++calls == 2)
            {
                second.SetResult();
            }

            return false;
        });
        using IdeaAutoEntryBackgroundService service = Service();

        await service.StartAsync(CancellationToken.None);
        await Task.Delay(100);
        calls.ShouldBe(1);
        _time.Advance(_settings.PollInterval);

        await second.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.StopAsync(CancellationToken.None);
    }

    [Test]
    public async Task Failed_request_is_logged_and_does_not_stop_the_lane()
    {
        var cause = new InvalidOperationException("The database is down.");
        var nextRan = new TaskCompletionSource();
        _sender.Send(Arg.Any<RunNextAutoEntryCommand>(), Arg.Any<CancellationToken>()).Returns(
            _ => throw cause,
            _ =>
            {
                nextRan.TrySetResult();

                return Task.FromResult(false);
            });
        ILogger log = Substitute.For<ILogger>();
        log.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        ILoggerFactory loggers = Substitute.For<ILoggerFactory>();
        loggers.CreateLogger(Arg.Any<string>()).Returns(log);
        using IdeaAutoEntryBackgroundService service = Service(new Logger<IdeaAutoEntryBackgroundService>(loggers));

        await service.StartAsync(CancellationToken.None);
        await AdvanceUntilAsync(nextRan.Task);
        await service.StopAsync(CancellationToken.None);

        object?[] logged = log.ReceivedCalls().Where(call => call.GetMethodInfo().Name == nameof(ILogger.Log)).ShouldHaveSingleItem().GetArguments();
        ((LogLevel)logged[0]!, (Exception?)logged[3]).ShouldBe((LogLevel.Error, cause));
    }

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

    private IdeaAutoEntryBackgroundService Service(ILogger<IdeaAutoEntryBackgroundService>? logger = null)
    {
        return new IdeaAutoEntryBackgroundService(
            _services.GetRequiredService<IServiceScopeFactory>(),
            _settings,
            _time,
            logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<IdeaAutoEntryBackgroundService>.Instance);
    }
}