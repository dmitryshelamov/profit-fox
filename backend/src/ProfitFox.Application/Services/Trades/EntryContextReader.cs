using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.Exchange;
using ProfitFox.Application.Ports.Security;
using ProfitFox.Domain.Entities.Accounts;
using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Accounts;
using ProfitFox.Domain.ValueObjects.Accounts;
using ProfitFox.Domain.ValueObjects.Trades;

namespace ProfitFox.Application.Services.Trades;

/// <summary>
///     Reads everything an entry is counted from, anew for each count (change take-idea-trades, design.md, decision 7): the
///     idea and the settings from the base; the account asked for or that of the settings, or the first one; the amount asked
///     for, or the default one, or the limit of a trade; from Bybit the last price and the rules of the pair, and with the key
///     of the account its permissions, its margin mode, the positions of the pair and its free USDT
///     (<see cref="IExchangeKeyInspector.FreeAsync" />); and the counters of the base. A key Bybit does not take, or one that
///     may withdraw, leaves the rest of the account unread, and the checks name it.
/// </summary>
public sealed class EntryContextReader(
    IIdeaRepository ideas,
    ITradeSettingsRepository settings,
    IExchangeAccountRepository accounts,
    ITradeRepository trades,
    ITradeHaltRepository halt,
    ITraderLeaseRepository trader,
    IExchangeMarket market,
    IExchangeTrading trading,
    IExchangeKeyInspector keys,
    ISecretProtector protector,
    TimeProvider time)
{
    /// <exception cref="KeyNotFoundException">There is no idea or no account of the id.</exception>
    /// <exception cref="ExchangeUnavailableException">Bybit did not answer.</exception>
    /// <exception cref="ExchangeException">Bybit refused a read for another reason.</exception>
    public async Task<EntryContext> ReadAsync(Guid ideaId, Guid? accountId, decimal? amount, CancellationToken cancellationToken)
    {
        Idea idea = await ideas.FindAsync(ideaId, cancellationToken) ?? throw new KeyNotFoundException($"There is no idea {ideaId}.");
        TradeSettings risk = await settings.GetAsync(cancellationToken);
        ExchangeAccount? account = await Account(accountId ?? risk.DefaultAccountId, accountId is not null, cancellationToken);
        decimal chosen = amount ?? risk.DefaultAmount ?? risk.MaxAmount ?? 0m;

        decimal price = await market.PriceAsync(idea.Symbol, cancellationToken) ?? 0m;
        PairRules? rules = await market.RulesAsync(idea.Symbol, cancellationToken);
        (EntryAccount? state, ExchangeCredentials? credentials) = account is null ? (null, null) : await Read(account, idea.Symbol, cancellationToken);

        (int open, decimal openAmount, bool taken) = await trades.CountersAsync(account?.Id, idea.Symbol, cancellationToken);
        TradeHalt ban = await halt.GetAsync(cancellationToken);
        bool alive = (await trader.StateAsync(cancellationToken))?.Alive ?? false;
        var input = new EntryInput(idea, risk, chosen, state, price, rules, new EntryCounters(ban.Halted, alive, open, openAmount, taken), time.GetUtcNow());
        return new EntryContext(input, account, credentials);
    }

    private async Task<ExchangeAccount?> Account(Guid? id, bool asked, CancellationToken cancellationToken)
    {
        if (id is { } wanted)
        {
            ExchangeAccount? found = await accounts.FindAsync(wanted, cancellationToken);
            if (found is not null || asked)
            {
                return found ?? throw new KeyNotFoundException($"There is no account {wanted}.");
            }
        }

        return (await accounts.AllAsync(cancellationToken)).FirstOrDefault();
    }

    private async Task<(EntryAccount, ExchangeCredentials?)> Read(ExchangeAccount account, string symbol, CancellationToken cancellationToken)
    {
        ExchangeCredentials credentials;
        try
        {
            ProtectedSecret secret = await accounts.SecretAsync(account.Id, cancellationToken) ?? throw new KeyNotFoundException($"There is no account {account.Id}.");
            credentials = new ExchangeCredentials(account.ApiKey, protector.Reveal(account.Id, secret));
        }
        catch (SecretUnreadableException)
        {
            return (Refused(account, "Секрет аккаунта не читается: удалите аккаунт и добавьте его заново"), null);
        }

        try
        {
            ExchangeKeyInfo key = await keys.InspectAsync(credentials.ApiKey, credentials.Secret, cancellationToken);
            if (ExchangeKeyRules.Check(key) is { } problem)
            {
                return (Refused(account, problem.Code == "WithdrawPermission" ? "У ключа аккаунта есть право вывода" : $"Ключ аккаунта не подходит: {problem.Reason}"), credentials);
            }
        }
        catch (ExchangeKeyRejectedException exception)
        {
            return (Refused(account, $"Bybit не принимает ключ аккаунта: {exception.Message}"), credentials);
        }

        string? mode = await keys.MarginModeAsync(credentials.ApiKey, credentials.Secret, cancellationToken);
        await accounts.SetMarginModeAsync(account.Id, mode, cancellationToken);
        IReadOnlyList<ExchangePosition> positions = await trading.PositionsAsync(credentials, symbol, cancellationToken);
        decimal free = await keys.FreeAsync(credentials.ApiKey, credentials.Secret, cancellationToken);
        return (new EntryAccount(account.Id, account.Name, null, mode, positions, free), credentials);
    }

    private static EntryAccount Refused(ExchangeAccount account, string reason)
    {
        return new EntryAccount(account.Id, account.Name, reason, null, [], null);
    }
}

/// <summary>
///     What an entry was counted from, with the account and its key for the calls that follow; the key is null when the secret
///     could not be read.
/// </summary>
public sealed record EntryContext(EntryInput Input, ExchangeAccount? Account, ExchangeCredentials? Credentials);
