using System.Globalization;

using MediatR;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.MarketData;
using ProfitFox.Application.Ports.ExchangeAccounts;
using ProfitFox.Application.Ports.LiveTrading;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Application.Ports.Security;
using ProfitFox.Application.Transactions;
using ProfitFox.Domain.Entities.Bots;
using ProfitFox.Domain.Entities.ExchangeAccounts;
using ProfitFox.Domain.Entities.LiveTrading;
using ProfitFox.Domain.Entities.LiveTrading.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Entities.Strategies;
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
    IExchangeLedger ledger,
    IInstrumentCatalog catalog,
    ILiveBotRepository live,
    ITransactionExecutor executor,
    TimeProvider time) : IRequestHandler<StartBotCommand>
{
    /// <summary>
    ///     Checks the account on the exchange first, then starts the bot under its lock on the latest version of its strategy;
    ///     a Worker does the trading.
    /// </summary>
    /// <exception cref="NotFoundException">There is no such bot.</exception>
    /// <exception cref="ConflictException">The bot works, or the account cannot trade it: the reason says why.</exception>
    /// <exception cref="ExchangeUnavailableException">The exchange did not answer.</exception>
    public async Task Handle(StartBotCommand request, CancellationToken cancellationToken)
    {
        StrategyLibrary loaded = await library.LoadAsync(cancellationToken);
        Bot bot = loaded.Bots.SingleOrDefault(bot => bot.Id == request.Id) ?? throw new NotFoundException($"There is no bot {request.Id}.");
        ExchangeAccount account = (bot.ExchangeAccountId is { } id ? await accounts.GetAsync(id, cancellationToken) : null)
                                  ?? throw new ConflictException("The bot has no exchange account.");
        ExchangeKey key = await LiveRequests.KeyAsync(accounts, protector, account, cancellationToken);
        await CheckKeyAsync(bot, key, cancellationToken);
        string quote = await QuoteCoinAsync(bot.Instrument, cancellationToken);
        await CheckFundsAsync(bot, key, quote, cancellationToken);

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

                return locked.Start(loaded.LatestVersion(bot.StrategyId)!.Number, loaded.ParameterValues(bot));
            },
            cancellationToken);
    }

    private async Task CheckKeyAsync(Bot bot, ExchangeKey key, CancellationToken cancellationToken)
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

        if (!StartFunds.Enough(free, bot.Balance, others))
        {
            throw new ConflictException(
                $"Not enough free money on the account: {free.ToString(CultureInfo.InvariantCulture)} {quote} for the bot and the other working bots of the account.");
        }
    }

    private async Task<string> QuoteCoinAsync(Instrument instrument, CancellationToken cancellationToken)
    {
        IReadOnlyList<ListedInstrument> listed = await catalog.ListAsync(instrument.Category, cancellationToken);

        return listed.SingleOrDefault(item => item.Symbol == instrument.Symbol)?.QuoteCoin
               ?? throw new ConflictException($"Bybit no longer lists {instrument.Symbol} in {instrument.Category.ToCode()}.");
    }
}