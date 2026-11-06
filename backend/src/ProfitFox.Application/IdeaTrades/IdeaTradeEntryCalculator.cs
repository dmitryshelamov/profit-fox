using ProfitFox.Application.Backtests;
using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ideas;
using ProfitFox.Application.LiveTrading;
using ProfitFox.Application.Ports.ExchangeAccounts;
using ProfitFox.Application.Ports.LiveTrading;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Application.Ports.Security;
using ProfitFox.Domain.Entities.Bots;
using ProfitFox.Domain.Entities.ExchangeAccounts;
using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.IdeaTrades;
using ProfitFox.Domain.Entities.IdeaTrades.Enums;
using ProfitFox.Domain.Entities.LiveTrading;
using ProfitFox.Domain.Entities.LiveTrading.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.Services.IdeaTrades;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.IdeaTrades;
using ProfitFox.Domain.ValueObjects.LiveTrading;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.IdeaTrades;

/// <summary>
///     The entry of a trade by an idea, in two steps so that the entry can be made under the lock of the account: <see cref="PrepareAsync" />
///     asks the exchange and reads the settings, <see cref="EvaluateAsync" /> reads the money and the other trades and bots of the account and
///     sizes the trade. It places nothing.
/// </summary>
public sealed class IdeaTradeEntryCalculator(
    IIdeaRepository ideas,
    IIdeaTradingSettingsRepository settings,
    IIdeaTradeRepository trades,
    IExchangeAccountRepository accounts,
    ISecretProtector protector,
    IExchangeKeyInspector inspector,
    IExchangeLedger ledger,
    IMarketHistoryClient history,
    IInstrumentRulesProvider rules,
    IInstrumentCatalog catalog,
    ILiveBotRepository live,
    IStrategyLibraryRepository library,
    IIdeaBucketRepository buckets,
    IdeasEngineVersion version,
    BacktestSettings backtests)
{
    /// <returns><c>null</c> when there is no such idea.</returns>
    /// <exception cref="ValidationFailedException">An unknown account, or an amount not above zero.</exception>
    /// <exception cref="ExchangeException">The exchange did not answer.</exception>
    public async Task<EntryPreparation?> PrepareAsync(Guid ideaId, Guid? accountId, decimal? amount, CancellationToken cancellationToken)
    {
        Idea? idea = await ideas.GetAsync(ideaId, cancellationToken);

        if (idea is null)
        {
            return null;
        }

        IdeaTradingSettings saved = await settings.GetAsync(cancellationToken);
        decimal asked = amount ?? saved.Amount;

        if (asked <= 0m)
        {
            throw new ValidationFailedException(new Dictionary<string, string> { ["amount"] = "The amount must be above zero." });
        }

        Guid? chosen = accountId ?? saved.AccountId;
        ExchangeAccount? account = chosen is { } id ? await accounts.GetAsync(id, cancellationToken) : null;

        if (accountId is not null && account is null)
        {
            throw new ValidationFailedException(new Dictionary<string, string> { ["accountId"] = "There is no such exchange account." });
        }

        Instrument instrument = idea.Instrument;
        (ExchangeKey? key, AccountAccess access) = await AccessAsync(account, instrument, cancellationToken);
        decimal current = await history.GetLastPriceAsync(instrument, cancellationToken);
        decimal price = idea.Track.Plan.Type == IdeaType.Pullback ? idea.Track.Plan.Entry : current;
        InstrumentRules instrumentRules = await rules.GetRulesAsync(instrument, cancellationToken);
        ListedInstrument listed = (await catalog.ListAsync(instrument.Category, cancellationToken)).Single(item => item.Symbol == instrument.Symbol);

        return new EntryPreparation(idea, saved, account, key, access, price, current, instrumentRules, backtests.Policy().DefaultFees[instrument.Category], asked, listed.QuoteCoin, listed.BaseCoin,
            await buckets.GetAsync(version.Number, cancellationToken));
    }

    /// <summary>Sizes the trade and names what stops it, by the books of the account as they are now.</summary>
    public async Task<EntryCalculation> EvaluateAsync(EntryPreparation preparation, CancellationToken cancellationToken)
    {
        Idea idea = preparation.Idea;
        IdeaPlan plan = idea.Track.Plan with { Entry = preparation.Price };
        IdeaTradeSize size = IdeaTradeSizing.Calculate(plan, preparation.Fees, preparation.Amount, preparation.Rules);
        IReadOnlyList<IdeaTrade> open = await trades.GetOpenAsync(cancellationToken);
        decimal? free = preparation.Key is { } key ? await ledger.GetFreeAsync(key, preparation.Quote, cancellationToken) : null;
        IdeaTradeAccount account = await AccountAsync(preparation, free, open, cancellationToken);
        var limits = new IdeaTradingLimits(preparation.Settings.MinRiskReward, preparation.Settings.MaxOpenTrades, preparation.Settings.MaxTotalRisk, preparation.Settings.MaxAmount);
        IReadOnlyList<IdeaTradeRefusal> reasons = IdeaTradeRefusals.Check(
            new IdeaTradeEntry(idea.Instrument.Category, idea.Track.Status, idea.Track.Plan, preparation.Price, preparation.Current, preparation.Amount, size, preparation.Rules),
            limits,
            new OpenIdeaTrades(open.Count, open.Sum(trade => trade.Risk)),
            account);

        return new EntryCalculation(preparation, plan, size, free, reasons, PromiseOf(idea with { Track = idea.Track with { Plan = plan } }, preparation));
    }

    /// <summary>
    ///     What the idea promises at this entry: the statistics of its bucket as the list of ideas shows them, and the break-even and the reward to
    ///     risk after the fees from the price of the entry, which <paramref name="idea" /> carries in its plan.
    /// </summary>
    private static IdeaPromise PromiseOf(Idea idea, EntryPreparation preparation)
    {
        IdeaView view = IdeaView.Of([idea], preparation.Buckets, new Dictionary<MarketCategory, FeeRates> { [idea.Instrument.Category] = preparation.Fees })[0];

        return new IdeaPromise(view.Bucket?.Badge, view.Probability, view.Bucket?.Expectancy, view.Bucket?.Cases, view.Shares.BreakEven, view.Shares.NetRiskReward);
    }

    private async Task<(ExchangeKey? Key, AccountAccess Access)> AccessAsync(ExchangeAccount? account, Instrument instrument, CancellationToken cancellationToken)
    {
        if (account is null)
        {
            return (null, AccountAccess.NoKey);
        }

        ExchangeKey key;

        try
        {
            key = await LiveRequests.KeyAsync(accounts, protector, account, cancellationToken);
        }
        catch (ConflictException)
        {
            return (null, AccountAccess.NoKey);
        }

        try
        {
            ExchangeKeyInfo info = await inspector.InspectAsync(key.ApiKey, key.Secret, cancellationToken);

            if (!info.TradableCategories.Contains(instrument.Category))
            {
                return (key, AccountAccess.CategoryNotTraded);
            }

            return instrument.Category == MarketCategory.Linear && await ledger.IsHedgeModeAsync(key, instrument, cancellationToken)
                ? (key, AccountAccess.HedgeMode)
                : (key, AccountAccess.Ready);
        }
        catch (ExchangeKeyRejectedException)
        {
            return (null, AccountAccess.NoKey);
        }
    }

    /// <summary>The working bots of the account with their money, the entries that wait, and the directions on the symbol of <c>linear</c>.</summary>
    private async Task<IdeaTradeAccount> AccountAsync(EntryPreparation preparation, decimal? free, IReadOnlyList<IdeaTrade> open, CancellationToken cancellationToken)
    {
        if (preparation.Account is not { } account)
        {
            // Without an account there is no money to count: the missing key is the reason, not the money.
            return new IdeaTradeAccount(preparation.Access, decimal.MaxValue, [], [], []);
        }

        Instrument instrument = preparation.Idea.Instrument;
        IReadOnlyDictionary<Guid, LiveBot> started = await live.GetAllAsync(cancellationToken);
        var funds = new List<BotFunds>();
        var directions = new List<StrategyDirection>();

        foreach (Bot bot in (await library.GetBotsAsync(cancellationToken)).Where(bot => bot.ExchangeAccountId == account.Id))
        {
            LiveBot? state = started.GetValueOrDefault(bot.Id);

            if (state?.Status is not (LiveBotStatus.Running or LiveBotStatus.Paused or LiveBotStatus.Stopping))
            {
                continue;
            }

            if (await QuoteAsync(bot.Instrument, cancellationToken) == preparation.Quote)
            {
                LivePosition? current = (await live.GetPositionsAsync(bot.Id, cancellationToken)).FirstOrDefault();
                funds.Add(new BotFunds(bot.Balance, current?.Invested ?? 0m));
            }

            if (instrument.Category == MarketCategory.Linear && bot.Instrument == instrument && state.Status != LiveBotStatus.Stopping && state.StrategyVersion is { } number)
            {
                directions.Add((await library.GetVersionAsync(bot.StrategyId, number, cancellationToken))!.Snapshot.Definition.Direction);
            }
        }

        IReadOnlyList<IdeaTrade> mine = [.. open.Where(trade => trade.AccountId == account.Id)];

        if (instrument.Category == MarketCategory.Linear)
        {
            directions.AddRange(mine.Where(trade => trade.Instrument == instrument).Select(trade => trade.Side == IdeaSide.Long ? StrategyDirection.Long : StrategyDirection.Short));
        }

        var pending = new List<decimal>();

        foreach (IdeaTrade trade in mine.Where(trade => trade.Status == IdeaTradeStatus.AwaitingEntry))
        {
            if (await QuoteAsync(trade.Instrument, cancellationToken) == preparation.Quote)
            {
                pending.Add(trade.Amount);
            }
        }

        return new IdeaTradeAccount(preparation.Access, free ?? decimal.MaxValue, funds, pending, directions);
    }

    private async Task<string?> QuoteAsync(Instrument instrument, CancellationToken cancellationToken)
    {
        return (await catalog.ListAsync(instrument.Category, cancellationToken)).SingleOrDefault(item => item.Symbol == instrument.Symbol)?.QuoteCoin;
    }
}

/// <summary>What <see cref="IdeaTradeEntryCalculator.PrepareAsync" /> learned: the idea, the settings, the account and what its key may do, the price the trade enters at, the price of the market now, the rules and the fees.</summary>
public sealed record EntryPreparation(
    Idea Idea,
    IdeaTradingSettings Settings,
    ExchangeAccount? Account,
    ExchangeKey? Key,
    AccountAccess Access,
    decimal Price,
    decimal Current,
    InstrumentRules Rules,
    FeeRates Fees,
    decimal Amount,
    string Quote,
    string BaseCoin,
    IReadOnlyList<IdeaBucket> Buckets);

/// <summary>The entry as it would be made now: the plan from the price, the size, the free money (none without a key), the reasons it cannot be made and what the idea promises.</summary>
public sealed record EntryCalculation(EntryPreparation Preparation, IdeaPlan Plan, IdeaTradeSize Size, decimal? FreeMoney, IReadOnlyList<IdeaTradeRefusal> Reasons, IdeaPromise Promise);