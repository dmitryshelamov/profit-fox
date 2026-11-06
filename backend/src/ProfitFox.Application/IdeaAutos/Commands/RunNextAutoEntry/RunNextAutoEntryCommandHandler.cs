using MediatR;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.IdeaTrades;
using ProfitFox.Application.Transactions;
using ProfitFox.Domain.Entities.IdeaAutos;
using ProfitFox.Domain.Entities.IdeaAutos.Enums;
using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.IdeaTrades;
using ProfitFox.Domain.Entities.IdeaTrades.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.IdeaAutos;

namespace ProfitFox.Application.IdeaAutos.Commands.RunNextAutoEntry;

public sealed class RunNextAutoEntryCommandHandler(
    IIdeaAutoEntryRepository entries,
    IIdeaAutoRepository autos,
    IIdeaAutoRefusalRepository refusals,
    IIdeaRepository ideas,
    IPaperIdeaTradeRepository paper,
    IIdeaTradeRepository trades,
    IdeaTradeEntryCalculator calculator,
    ILiveBotRepository live,
    ITransactionExecutor executor,
    TimeProvider time) : IRequestHandler<RunNextAutoEntryCommand, bool>
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(60);

    /// <summary>
    ///     Under the lock of the requests, which lets one Worker at a time work, takes the earliest request. One that waited more than 60 seconds
    ///     is refused «Не успел войти», and one of an auto that was deleted or is not «С деньгами» any more «Автомат выключен». Otherwise the entry
    ///     is made as <c>POST /api/idea-trades</c> makes it: asked of the exchange outside a transaction, then checked again under the lock of the
    ///     account and of the creation of trades, with the account, the amount and the lowest reward to risk the request carries.
    /// </summary>
    /// <returns>
    ///     <c>false</c> when there is no request, another Worker holds the lock, or the exchange did not answer: the request stays. An entry that
    ///     cannot be prepared for another reason is refused, «Нет ключа» for an account that is gone and «Ошибка подготовки входа» otherwise.
    /// </returns>
    public async Task<bool> Handle(RunNextAutoEntryCommand request, CancellationToken cancellationToken)
    {
        await using IAsyncDisposable? held = await entries.TryLockEntriesAsync(cancellationToken);

        if (held is null || await entries.GetNextAsync(cancellationToken) is not { } entry)
        {
            return false;
        }

        DateTimeOffset now = time.GetUtcNow();
        Idea? idea = await ideas.GetAsync(entry.IdeaId, cancellationToken);
        IdeaAuto? auto = await autos.GetAsync(entry.AutoId, cancellationToken);

        if (idea is null)
        {
            await executor.ExecuteAsync(async token => await DeleteAsync(entry, token), cancellationToken);

            return true;
        }

        if (now - entry.CreatedAt > Wait)
        {
            await RefuseAsync(entry, idea, [new AutoRefusal(IdeaTradeRefusal.Late)], now, cancellationToken);

            return true;
        }

        if (auto is not { DeletedAt: null, Mode: IdeaAutoMode.Live })
        {
            await RefuseAsync(entry, idea, [new AutoRefusal(IdeaTradeRefusal.AutoOff)], now, cancellationToken);

            return true;
        }

        EntryPreparation preparation;

        try
        {
            preparation = (await calculator.PrepareAsync(entry.IdeaId, entry.AccountId, entry.Amount, cancellationToken))!;
        }
        catch (ExchangeException)
        {
            return false;
        }
        catch (ValidationFailedException)
        {
            // The account of the request is gone: there is no key to enter with.
            await RefuseAsync(entry, idea, [new AutoRefusal(IdeaTradeRefusal.NoKey)], now, cancellationToken);

            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Tried again it would fail again and hold up every request behind it until they are all late.
            await RefuseAsync(entry, idea, [new AutoRefusal(IdeaTradeRefusal.EntryError)], now, cancellationToken);

            return true;
        }

        await executor.ExecuteAsync(async token => await EnterAsync(entry, idea, preparation, token), cancellationToken);

        return true;
    }

    private async Task<int> EnterAsync(IdeaAutoEntry entry, Idea idea, EntryPreparation preparation, CancellationToken cancellationToken)
    {
        // The money and the open trades of the account are checked for one entry or start at a time, as for a manual entry.
        await live.LockAccountAsync(entry.AccountId, cancellationToken);
        await trades.LockCreationAsync(cancellationToken);

        if (await entries.LockAsync(entry.Id, cancellationToken) is null)
        {
            return 0;
        }

        DateTimeOffset now = time.GetUtcNow();

        // The owner may have switched the auto off or deleted it while the exchange was asked; its row stays as read until the commit.
        if (await autos.LockAsync(entry.AutoId, cancellationToken) is not { DeletedAt: null, Mode: IdeaAutoMode.Live } locked)
        {
            return await WriteRefusalAsync(entry, idea, [new AutoRefusal(IdeaTradeRefusal.AutoOff)], now, cancellationToken);
        }

        // The entry goes on the account of the request, so the pair is held by the trades of that account, whatever the account of the auto is by now.
        var autoEntry = new AutoEntry(
            locked with { AccountId = entry.AccountId },
            entry.MinRiskReward,
            DailyLossReader.Of(await trades.GetAllAsync(cancellationToken), preparation.Settings.DailyLossLimit, now),
            await paper.GetOpenAsync(cancellationToken),
            await autos.GetEveryAsync(cancellationToken));
        EntryCalculation calculation = await calculator.EvaluateAsync(preparation, autoEntry, cancellationToken);

        if (calculation.Refusals.Count > 0)
        {
            return await WriteRefusalAsync(entry, idea, calculation.Refusals, now, cancellationToken);
        }

        IdeaTrade trade = IdeaTrade.Create(Guid.NewGuid(), idea, entry.AccountId, preparation.Price, calculation.Size, now, calculation.Promise, entry.AutoId) with { LastOrderNumber = 1 };
        await trades.CreateAsync(trade, IdeaTradeOrder.NewEntryOf(trade, now), now, cancellationToken);
        await entries.DeleteAsync(entry.Id, cancellationToken);

        return 0;
    }

    private Task RefuseAsync(IdeaAutoEntry entry, Idea idea, IReadOnlyList<AutoRefusal> reasons, DateTimeOffset now, CancellationToken cancellationToken)
    {
        return executor.ExecuteAsync(
            async token =>
            {
                if (await entries.LockAsync(entry.Id, token) is null)
                {
                    return 0;
                }

                return await WriteRefusalAsync(entry, idea, reasons, now, token);
            },
            cancellationToken);
    }

    private async Task<int> WriteRefusalAsync(IdeaAutoEntry entry, Idea idea, IReadOnlyList<AutoRefusal> reasons, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await refusals.AddAsync(
            new IdeaAutoRefusal(
                Guid.NewGuid(),
                entry.IdeaId,
                entry.AutoId,
                IdeaAutoMode.Live,
                now,
                idea.Instrument,
                idea.Setup,
                idea.Interval,
                idea.Track.Plan.Type,
                idea.Track.Plan.Side,
                [.. reasons.Select(reason => reason.Reason)],
                reasons[0].Detail),
            cancellationToken);
        await entries.DeleteAsync(entry.Id, cancellationToken);

        return 0;
    }

    private async Task<int> DeleteAsync(IdeaAutoEntry entry, CancellationToken cancellationToken)
    {
        await entries.LockAsync(entry.Id, cancellationToken);
        await entries.DeleteAsync(entry.Id, cancellationToken);

        return 0;
    }
}