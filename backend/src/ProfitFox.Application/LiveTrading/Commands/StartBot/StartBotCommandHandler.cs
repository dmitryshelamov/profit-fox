using System.Globalization;

using MediatR;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.ExchangeAccounts;
using ProfitFox.Application.MarketData;
using ProfitFox.Application.Ports.ExchangeAccounts;
using ProfitFox.Application.Ports.LiveTrading;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Application.Ports.Security;
using ProfitFox.Application.Transactions;
using ProfitFox.Domain.Entities.Bots;
using ProfitFox.Domain.Entities.ExchangeAccounts;
using ProfitFox.Domain.Entities.ExchangeAccounts.Enums;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.IdeaTrades;
using ProfitFox.Domain.Entities.IdeaTrades.Enums;
using ProfitFox.Domain.Entities.LiveTrading;
using ProfitFox.Domain.Entities.LiveTrading.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.LiveTrading;
using ProfitFox.Domain.ValueObjects.LiveTrading;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.LiveTrading.Commands.StartBot;

public sealed class StartBotCommandHandler(
    IStrategyLibraryRepository library,
    IExchangeAccountRepository accounts,
    ISecretProtector protector,
    IExchangeKeyInspector inspector,
    IBrokerAccountReader broker,
    IExchangeLedger ledger,
    IInstrumentCatalog catalog,
    ILiveBotRepository live,
    IIdeaTradeRepository ideaTrades,
    ITransactionExecutor executor,
    MoexTradingSettings moex,
    TimeProvider time) : IRequestHandler<StartBotCommand>
{
    /// <summary>
    ///     Checks the key on the exchange first, then, under the lock of the bot and of its account, the money and starts the bot
    ///     on the latest version of its strategy; a Worker does the trading.
    /// </summary>
    /// <exception cref="NotFoundException">There is no such bot.</exception>
    /// <exception cref="ConflictException">The bot works, the account cannot trade it, or the trading on MOEX is off: the reason says why.</exception>
    /// <exception cref="ExchangeUnavailableException">The exchange did not answer.</exception>
    public async Task Handle(StartBotCommand request, CancellationToken cancellationToken)
    {
        StrategyLibrary loaded = await library.LoadAsync(cancellationToken);
        Bot bot = loaded.Bots.SingleOrDefault(bot => bot.Id == request.Id) ?? throw new NotFoundException($"There is no bot {request.Id}.");

        // Decision 1: while the trading on MOEX is off, no bot of moex starts, and nothing is asked of any exchange.
        if (bot.Instrument.Category.Exchange() == Exchange.TInvest && !moex.Enabled)
        {
            throw new ConflictException("Trading on MOEX is disabled.");
        }

        ExchangeAccount account = (bot.ExchangeAccountId is { } id ? await accounts.GetAsync(id, cancellationToken) : null)
                                  ?? throw new ConflictException("The bot has no exchange account.");
        ExchangeKey key = await LiveRequests.KeyAsync(accounts, protector, account, cancellationToken);

        // Decision 10: an account of the broker is checked by its token, an API key with its secret at its exchange.
        if (key.Exchange == Exchange.TInvest)
        {
            await CheckBrokerAccountAsync(key, cancellationToken);
        }
        else
        {
            await CheckApiKeyAsync(bot, key, cancellationToken);
        }
        string quote = await QuoteCoinAsync(bot.Instrument, cancellationToken);

        await LiveRequests.ChangeAsync(
            executor,
            live,
            time,
            bot.Id,
            async (locked, token) =>
            {
                // A change saved before the lock was taken is seen now; the bot checked above must be the bot that starts.
                if ((await library.GetBotAsync(bot.Id, token))?.UpdatedAt != bot.UpdatedAt)
                {
                    throw new ConflictException("The bot changed while it was starting; start it again.");
                }

                // Under the lock of the account another start waits, so it sees this one among the working bots.
                await live.LockAccountAsync(account.Id, token);
                await CheckTradesAsync(bot, loaded.LatestVersion(bot.StrategyId)!.Snapshot.Definition.Direction, token);
                await CheckFundsAsync(bot, key, quote, token);

                return locked.Start(loaded.LatestVersion(bot.StrategyId)!.Number, loaded.ParameterValues(bot));
            },
            cancellationToken);
    }

    private async Task CheckApiKeyAsync(Bot bot, ExchangeKey key, CancellationToken cancellationToken)
    {
        try
        {
            ExchangeKeyInfo info = await inspector.InspectAsync(key.ApiKey, key.Secret, cancellationToken);

            if (!info.TradableCategories.Contains(bot.Instrument.Category))
            {
                throw new ConflictException($"The key of the account does not trade in {bot.Instrument.Category.ToCode()}.");
            }

            if (bot.Instrument.Category == MarketCategory.Linear && await ledger.IsHedgeModeAsync(key, bot.Instrument, cancellationToken))
            {
                throw new ConflictException($"The account keeps {bot.Instrument.Symbol} in hedge mode; switch it to one-way mode.");
            }
        }
        catch (ExchangeKeyRejectedException exception)
        {
            throw new ConflictException(exception.Message);
        }
    }

    /// <summary>The token of the key must still open its account, open and with full access.</summary>
    private async Task CheckBrokerAccountAsync(ExchangeKey key, CancellationToken cancellationToken)
    {
        IReadOnlyList<BrokerAccount> opened;

        try
        {
            opened = await broker.ListAccountsAsync(key.Secret, cancellationToken);
        }
        catch (ExchangeKeyRejectedException exception)
        {
            throw new ConflictException(exception.Message);
        }

        BrokerAccount account = opened.SingleOrDefault(item => item.Id == key.ApiKey)
                                ?? throw new ConflictException($"The token no longer opens the account {key.ApiKey}.");

        if (BrokerAccountRules.Problem(account) is { } problem)
        {
            throw new ConflictException(BrokerAccountRules.Describe(problem));
        }
    }

    /// <summary>Other working bots of the account in the same quote coin take what they have not invested of their balance.</summary>
    private async Task CheckFundsAsync(Bot bot, ExchangeKey key, string quote, CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<Guid, LiveBot> started = await live.GetAllAsync(cancellationToken);
        var others = new List<BotFunds>();

        foreach (Bot other in (await library.GetBotsAsync(cancellationToken)).Where(other => other.Id != bot.Id && other.ExchangeAccountId == bot.ExchangeAccountId))
        {
            if (started.GetValueOrDefault(other.Id)?.Status is LiveBotStatus.Running or LiveBotStatus.Paused or LiveBotStatus.Stopping
                && await QuoteCoinAsync(other.Instrument, cancellationToken) == quote)
            {
                LivePosition? current = (await live.GetPositionsAsync(other.Id, cancellationToken)).FirstOrDefault();
                others.Add(new BotFunds(other.Balance, current?.Invested ?? 0m));
            }
        }

        decimal free;

        try
        {
            free = await ledger.GetFreeAsync(key, quote, cancellationToken);
        }
        catch (ExchangeKeyRejectedException exception)
        {
            throw new ConflictException(exception.Message);
        }

        if (!StartFunds.Enough(free, bot.Balance, others, await PendingEntriesAsync(bot, quote, cancellationToken)))
        {
            throw new ConflictException(
                $"Not enough free money on the account: {free.ToString(CultureInfo.InvariantCulture)} {quote} for the bot and the other working bots of the account.");
        }
    }

    /// <summary>A trade by an idea of the account that is open on the symbol of a bot on <c>linear</c> must not be of the other side than the bot.</summary>
    private async Task CheckTradesAsync(Bot bot, StrategyDirection direction, CancellationToken cancellationToken)
    {
        if (bot.Instrument.Category == MarketCategory.Linear
            && (await ideaTrades.GetOpenAsync(cancellationToken)).Any(trade => trade.AccountId == bot.ExchangeAccountId
                                                                               && trade.Instrument == bot.Instrument
                                                                               && (trade.Side == IdeaSide.Long ? StrategyDirection.Long : StrategyDirection.Short) != direction))
        {
            throw new ConflictException($"There is an open trade by an idea of the opposite side on {bot.Instrument.Symbol}.");
        }
    }

    /// <summary>The entries of trades by ideas of the account that wait to be filled, in the quote coin of the bot.</summary>
    private async Task<IReadOnlyCollection<decimal>> PendingEntriesAsync(Bot bot, string quote, CancellationToken cancellationToken)
    {
        var amounts = new List<decimal>();

        foreach (IdeaTrade trade in (await ideaTrades.GetOpenAsync(cancellationToken)).Where(trade => trade.AccountId == bot.ExchangeAccountId && trade.Status == IdeaTradeStatus.AwaitingEntry))
        {
            if (await QuoteCoinAsync(trade.Instrument, cancellationToken) == quote)
            {
                amounts.Add(trade.Amount);
            }
        }

        return amounts;
    }

    private async Task<string> QuoteCoinAsync(Instrument instrument, CancellationToken cancellationToken)
    {
        IReadOnlyList<ListedInstrument> listed = await catalog.ListAsync(instrument.Category, cancellationToken);

        return listed.SingleOrDefault(item => item.Symbol == instrument.Symbol)?.QuoteCoin
               ?? throw new ConflictException($"{instrument.Category.Exchange().DisplayName()} no longer lists {instrument.Symbol} in {instrument.Category.ToCode()}.");
    }
}