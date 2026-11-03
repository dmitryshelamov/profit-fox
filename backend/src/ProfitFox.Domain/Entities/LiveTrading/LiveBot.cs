using ProfitFox.Domain.Entities.LiveTrading.Enums;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Domain.Entities.LiveTrading;

/// <summary>
///     A bot in live trading: its status, the strategy version and parameter values fixed when it started, and what the Worker
///     that keeps it needs to go on. <see cref="StartPending" /> gives the strategy its start event once; <see cref="CloseRequested" />
///     asks a Worker to close the position of a stopped bot; <see cref="SessionFailures" /> counts the failures in a row to take it up,
///     and <see cref="SessionError" /> tells why a bot with orders on the exchange is still not taken up.
/// </summary>
public sealed record LiveBot(
    Guid BotId,
    LiveBotStatus Status,
    string? StopReason,
    int? StrategyVersion,
    IReadOnlyDictionary<string, NodeValue> ParameterValues,
    bool StartPending,
    bool CloseRequested,
    int LastOrderNumber,
    DateTimeOffset? LastBarAt,
    int SessionFailures = 0,
    string? SessionError = null)
{
    // How many failures in a row to take the bot up a Worker makes before the bot fails.
    private const int MaxSessionFailures = 3;

    public static LiveBot New(Guid botId)
    {
        return new LiveBot(botId, LiveBotStatus.Stopped, null, null, new Dictionary<string, NodeValue>(), false, false, 0, null);
    }

    /// <summary>A Worker keeps a bot that runs, waits for its cancels or closes its position.</summary>
    public bool Kept => Status is LiveBotStatus.Running or LiveBotStatus.Paused or LiveBotStatus.Stopping || CloseRequested;

    /// <summary>The settings of a bot change only while nothing trades with them.</summary>
    public bool Editable => Status is LiveBotStatus.Stopped or LiveBotStatus.Failed;

    /// <summary>The bot runs and has not yet traded the candle that opened at <paramref name="openTime" />.</summary>
    public bool Awaits(DateTimeOffset openTime)
    {
        return Status == LiveBotStatus.Running && openTime > (LastBarAt ?? DateTimeOffset.MinValue);
    }

    /// <exception cref="LiveBotConflictException">The bot works or closes its position.</exception>
    public LiveBot Start(int strategyVersion, IReadOnlyDictionary<string, NodeValue> parameterValues)
    {
        if (!Editable || CloseRequested)
        {
            throw new LiveBotConflictException("Only a stopped bot starts.");
        }

        return this with
        {
            Status = LiveBotStatus.Running,
            StopReason = null,
            StrategyVersion = strategyVersion,
            ParameterValues = parameterValues,
            StartPending = true,
            SessionFailures = 0,
            SessionError = null
        };
    }

    /// <exception cref="LiveBotConflictException">The bot does not run.</exception>
    public LiveBot Pause()
    {
        return Status == LiveBotStatus.Running
            ? this with { Status = LiveBotStatus.Paused }
            : throw new LiveBotConflictException("Only a running bot pauses.");
    }

    /// <exception cref="LiveBotConflictException">The bot is not on pause.</exception>
    public LiveBot Resume()
    {
        return Status == LiveBotStatus.Paused
            ? this with { Status = LiveBotStatus.Running }
            : throw new LiveBotConflictException("Only a paused bot resumes.");
    }

    /// <exception cref="LiveBotConflictException">The bot neither runs nor is on pause.</exception>
    public LiveBot Stop()
    {
        return Status is LiveBotStatus.Running or LiveBotStatus.Paused
            ? this with { Status = LiveBotStatus.Stopping }
            : throw new LiveBotConflictException("Only a running or paused bot stops.");
    }

    /// <summary>Stops the bot as <see cref="Stop" /> does; the first reason stays.</summary>
    public LiveBot Fail(string reason)
    {
        return this with { Status = LiveBotStatus.Stopping, StopReason = StopReason ?? reason };
    }

    /// <summary>Every cancel of a stopping bot is confirmed.</summary>
    public LiveBot Stopped()
    {
        return this with { Status = StopReason is null ? LiveBotStatus.Stopped : LiveBotStatus.Failed };
    }

    /// <exception cref="LiveBotConflictException">The bot works, or it has no open position.</exception>
    public LiveBot RequestClose(bool hasOpenPosition)
    {
        if (!Editable)
        {
            throw new LiveBotConflictException("Only the position of a stopped bot is closed.");
        }

        return hasOpenPosition ? this with { CloseRequested = true, SessionFailures = 0, SessionError = null } : throw new LiveBotConflictException("The bot has no open position.");
    }

    public LiveBot Closed()
    {
        return this with { CloseRequested = false };
    }

    /// <summary>
    ///     A Worker failed to take the bot up. The third failure in a row stops trying: the bot fails with the reason and gives up a
    ///     close of its position, so no Worker takes it again until the owner starts it.
    /// </summary>
    /// <summary>
    ///     A Worker failed to take the bot up. The third failure in a row fails a bot with nothing on the exchange; a bot whose
    ///     orders are on the exchange is never left unwatched: it stays kept, with the reason for the owner to see.
    /// </summary>
    public LiveBot SessionFailed(string reason, bool holdsOrders)
    {
        int failures = SessionFailures + 1;

        if (failures < MaxSessionFailures)
        {
            return this with { SessionFailures = failures };
        }

        return holdsOrders
            ? this with
            {
                SessionFailures = failures,
                SessionError = $"The Worker failed to take the bot {failures} times in a row and keeps trying, as its orders are on the exchange: {reason}"
            }
            : this with
            {
                Status = LiveBotStatus.Failed,
                StopReason = StopReason ?? $"The Worker failed to take the bot {failures} times in a row: {reason}",
                CloseRequested = false,
                SessionFailures = failures
            };
    }

    /// <summary>A Worker took the bot up: the failures before are forgotten.</summary>
    public LiveBot SessionUp()
    {
        return this with { SessionFailures = 0, SessionError = null };
    }

    public LiveBot Started()
    {
        return this with { StartPending = false };
    }
}