using ProfitFox.Domain.Entities.LiveTrading;
using ProfitFox.Domain.Entities.LiveTrading.Enums;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.ValueObjects.Strategies;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Entities.LiveTrading;

public class LiveBotTests
{
    private static readonly Guid Bot = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly IReadOnlyDictionary<string, NodeValue> Values = new Dictionary<string, NodeValue> { ["fast"] = new NumberValue(10m) };

    // A session that fails is counted; the bot goes on, another take tries again.
    [Test]
    public void Session_that_fails_is_counted()
    {
        LiveBot bot = LiveBot.New(Bot).Start(1, Values);

        LiveBot failed = bot.SessionFailed("Bybit refused the fee rates.", holdsOrders: false);

        (failed.Status, failed.SessionFailures, failed.StopReason, failed.SessionError).ShouldBe((LiveBotStatus.Running, 1, null, null));
    }

    // The third failure in a row of a bot with nothing on the exchange stops trying: the bot fails with the reason, and a
    // close of its position is given up too.
    [Test]
    public void Third_session_failure_in_a_row_fails_a_bot_without_orders()
    {
        LiveBot bot = LiveBot.New(Bot) with { Status = LiveBotStatus.Stopping, CloseRequested = true, SessionFailures = 2 };

        LiveBot failed = bot.SessionFailed("Bybit refused the fee rates.", holdsOrders: false);

        (failed.Status, failed.CloseRequested, failed.Kept).ShouldBe((LiveBotStatus.Failed, false, false));
        failed.StopReason.ShouldBe("The Worker failed to take the bot 3 times in a row: Bybit refused the fee rates.");
    }

    // A bot whose orders are on the exchange is never left unwatched: it stays kept, and the owner sees why it is not taken up.
    [Test]
    public void Third_session_failure_keeps_a_bot_with_orders_kept()
    {
        LiveBot bot = LiveBot.New(Bot).Start(1, Values) with { SessionFailures = 2 };

        LiveBot failing = bot.SessionFailed("Bybit refused the fee rates.", holdsOrders: true);

        (failing.Status, failing.Kept, failing.StopReason).ShouldBe((LiveBotStatus.Running, true, null));
        failing.SessionError.ShouldBe("The Worker failed to take the bot 3 times in a row and keeps trying, as its orders are on the exchange: Bybit refused the fee rates.");
    }

    // As with any failure, the first reason stays: the bot was stopping for it before its Worker failed.
    [Test]
    public void Third_session_failure_keeps_the_first_reason()
    {
        LiveBot bot = LiveBot.New(Bot) with { Status = LiveBotStatus.Stopping, StopReason = "Order c-7 of the bot is unknown.", SessionFailures = 2 };

        bot.SessionFailed("Bybit refused the fee rates.", holdsOrders: false).StopReason.ShouldBe("Order c-7 of the bot is unknown.");
    }

    [Test]
    public void Session_that_is_up_forgets_the_failures()
    {
        LiveBot bot = LiveBot.New(Bot).Start(1, Values) with { SessionFailures = 3, SessionError = "Failed." };

        LiveBot up = bot.SessionUp();

        (up.SessionFailures, up.SessionError).ShouldBe((0, null));
    }

    // A manual start or close begins the count again, as a manual retry of an import or a backtest does.
    [Test]
    public void Owner_actions_begin_the_count_again()
    {
        LiveBot failed = LiveBot.New(Bot) with { Status = LiveBotStatus.Failed, SessionFailures = 3, SessionError = "Failed." };

        (failed.Start(1, Values).SessionFailures, failed.Start(1, Values).SessionError).ShouldBe((0, null));
        failed.RequestClose(hasOpenPosition: true).SessionFailures.ShouldBe(0);
    }

    // Scenario "Новый бот".
    [Test]
    public void New_bot_is_stopped_without_a_reason()
    {
        var bot = LiveBot.New(Bot);

        (bot.Status, bot.StopReason, bot.StartPending, bot.CloseRequested).ShouldBe((LiveBotStatus.Stopped, null, false, false));
    }

    // Scenario "Запуск остановленного бота": the version and the values stay until the next start.
    [TestCase(LiveBotStatus.Stopped)]
    [TestCase(LiveBotStatus.Failed)]
    public void Start_fixes_the_version_and_the_values(LiveBotStatus from)
    {
        LiveBot bot = LiveBot.New(Bot) with { Status = from, StopReason = from == LiveBotStatus.Failed ? "Boom." : null };

        LiveBot started = bot.Start(3, Values);

        (started.Status, started.StopReason, started.StrategyVersion, started.StartPending).ShouldBe((LiveBotStatus.Running, null, 3, true));
        started.ParameterValues.ShouldBe(Values);
    }

    // Scenario "Повторный запуск".
    [TestCase(LiveBotStatus.Running)]
    [TestCase(LiveBotStatus.Paused)]
    [TestCase(LiveBotStatus.Stopping)]
    public void Start_of_a_working_bot_is_a_conflict(LiveBotStatus from)
    {
        Should.Throw<LiveBotConflictException>(() => (LiveBot.New(Bot) with { Status = from }).Start(3, Values));
    }

    [Test]
    public void Start_while_the_position_closes_is_a_conflict()
    {
        Should.Throw<LiveBotConflictException>(() => (LiveBot.New(Bot) with { CloseRequested = true }).Start(3, Values));
    }

    [Test]
    public void Pause_and_resume_switch_a_running_bot()
    {
        LiveBot paused = LiveBot.New(Bot).Start(3, Values).Pause();

        paused.Status.ShouldBe(LiveBotStatus.Paused);
        paused.Resume().Status.ShouldBe(LiveBotStatus.Running);
    }

    [TestCase(LiveBotStatus.Stopped)]
    [TestCase(LiveBotStatus.Paused)]
    [TestCase(LiveBotStatus.Stopping)]
    [TestCase(LiveBotStatus.Failed)]
    public void Pause_of_a_bot_that_does_not_run_is_a_conflict(LiveBotStatus from)
    {
        Should.Throw<LiveBotConflictException>(() => (LiveBot.New(Bot) with { Status = from }).Pause());
    }

    [TestCase(LiveBotStatus.Stopped)]
    [TestCase(LiveBotStatus.Running)]
    [TestCase(LiveBotStatus.Stopping)]
    [TestCase(LiveBotStatus.Failed)]
    public void Resume_of_a_bot_not_on_pause_is_a_conflict(LiveBotStatus from)
    {
        Should.Throw<LiveBotConflictException>(() => (LiveBot.New(Bot) with { Status = from }).Resume());
    }

    [TestCase(LiveBotStatus.Running)]
    [TestCase(LiveBotStatus.Paused)]
    public void Stop_waits_for_the_cancels(LiveBotStatus from)
    {
        LiveBot stopping = (LiveBot.New(Bot) with { Status = from }).Stop();

        stopping.Status.ShouldBe(LiveBotStatus.Stopping);
        stopping.Stopped().Status.ShouldBe(LiveBotStatus.Stopped);
    }

    [TestCase(LiveBotStatus.Stopped)]
    [TestCase(LiveBotStatus.Stopping)]
    [TestCase(LiveBotStatus.Failed)]
    public void Stop_of_a_bot_that_does_not_work_is_a_conflict(LiveBotStatus from)
    {
        Should.Throw<LiveBotConflictException>(() => (LiveBot.New(Bot) with { Status = from }).Stop());
    }

    // A failure stops the bot as "Остановить" does, and its reason stays with the failed bot.
    [TestCase(LiveBotStatus.Running)]
    [TestCase(LiveBotStatus.Paused)]
    [TestCase(LiveBotStatus.Stopping)]
    public void Failure_stops_the_bot_with_its_reason(LiveBotStatus from)
    {
        LiveBot failing = (LiveBot.New(Bot) with { Status = from }).Fail("Insufficient funds.");

        (failing.Status, failing.StopReason).ShouldBe((LiveBotStatus.Stopping, "Insufficient funds."));
        LiveBot failed = failing.Stopped();
        (failed.Status, failed.StopReason).ShouldBe((LiveBotStatus.Failed, "Insufficient funds."));
    }

    [Test]
    public void The_first_failure_keeps_its_reason()
    {
        LiveBot failing = LiveBot.New(Bot).Start(3, Values).Fail("First.").Fail("Second.");

        failing.StopReason.ShouldBe("First.");
    }

    // Scenario "Закрытие лонга".
    [TestCase(LiveBotStatus.Stopped)]
    [TestCase(LiveBotStatus.Failed)]
    public void Close_of_an_open_position_is_requested_of_a_stopped_bot(LiveBotStatus from)
    {
        LiveBot closing = (LiveBot.New(Bot) with { Status = from }).RequestClose(hasOpenPosition: true);

        (closing.Status, closing.CloseRequested).ShouldBe((from, true));
        closing.Closed().CloseRequested.ShouldBeFalse();
    }

    // Scenario "Запущенный бот" of the close.
    [TestCase(LiveBotStatus.Running)]
    [TestCase(LiveBotStatus.Paused)]
    [TestCase(LiveBotStatus.Stopping)]
    public void Close_of_a_working_bot_is_a_conflict(LiveBotStatus from)
    {
        Should.Throw<LiveBotConflictException>(() => (LiveBot.New(Bot) with { Status = from }).RequestClose(hasOpenPosition: true));
    }

    [Test]
    public void Close_without_an_open_position_is_a_conflict()
    {
        Should.Throw<LiveBotConflictException>(() => LiveBot.New(Bot).RequestClose(hasOpenPosition: false));
    }

    [Test]
    public void Start_event_is_given_once()
    {
        LiveBot.New(Bot).Start(3, Values).Started().StartPending.ShouldBeFalse();
    }

    // A Worker keeps a bot that runs, waits for its cancels or closes its position; nobody keeps a stopped bot.
    [TestCase(LiveBotStatus.Running, false, true)]
    [TestCase(LiveBotStatus.Paused, false, true)]
    [TestCase(LiveBotStatus.Stopping, false, true)]
    [TestCase(LiveBotStatus.Stopped, false, false)]
    [TestCase(LiveBotStatus.Failed, false, false)]
    [TestCase(LiveBotStatus.Stopped, true, true)]
    public void Kept_by_a_worker(LiveBotStatus status, bool closeRequested, bool kept)
    {
        (LiveBot.New(Bot) with { Status = status, CloseRequested = closeRequested }).Kept.ShouldBe(kept);
    }

    [TestCase(LiveBotStatus.Stopped, true)]
    [TestCase(LiveBotStatus.Failed, true)]
    [TestCase(LiveBotStatus.Running, false)]
    [TestCase(LiveBotStatus.Paused, false)]
    [TestCase(LiveBotStatus.Stopping, false)]
    public void Only_a_stopped_bot_can_be_changed(LiveBotStatus status, bool editable)
    {
        (LiveBot.New(Bot) with { Status = status }).Editable.ShouldBe(editable);
    }
}