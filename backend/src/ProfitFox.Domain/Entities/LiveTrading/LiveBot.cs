using ProfitFox.Domain.Entities.LiveTrading.Enums;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Domain.Entities.LiveTrading;

/// <summary>
///     A bot in live trading: its status, the strategy version and parameter values fixed when it started, and what the Worker
///     that keeps it needs to go on. <see cref="StartPending" /> gives the strategy its start event once; <see cref="CloseRequested" />
///     asks a Worker to close the position of a stopped bot.
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
    DateTimeOffset? LastBarAt)
{
    public static LiveBot New(Guid botId)
    {
        return new LiveBot(botId, LiveBotStatus.Stopped, null, null, new Dictionary<string, NodeValue>(), false, false, 0, null);
    }

    /// <summary>A Worker keeps a bot that runs, waits for its cancels or closes its position.</summary>
    public bool Kept => Status is LiveBotStatus.Running or LiveBotStatus.Paused or LiveBotStatus.Stopping || CloseRequested;

    /// <summary>The settings of a bot change only while nothing trades with them.</summary>
    public bool Editable => Status is LiveBotStatus.Stopped or LiveBotStatus.Failed;

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
            StartPending = true
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

        return hasOpenPosition ? this with { CloseRequested = true } : throw new LiveBotConflictException("The bot has no open position.");
    }

    public LiveBot Closed()
    {
        return this with { CloseRequested = false };
    }

    public LiveBot Started()
    {
        return this with { StartPending = false };
    }
}