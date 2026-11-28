using MediatR;

using ProfitFox.Application.Exceptions;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Trades;
using ProfitFox.Domain.ValueObjects.Trades;

namespace ProfitFox.Application.Commands.Trades.SaveTradeSettings;

/// <summary>
///     Saves the settings of the risk module in place of those before (spec trade-risk «Настройки риска»): they act on the
///     next entries, the trades already there keep their numbers. The moment of the save is kept to the microsecond, as the
///     store keeps it.
/// </summary>
public sealed record SaveTradeSettingsCommand(TradeSettings Settings) : IRequest<TradeSettings>;

internal sealed class SaveTradeSettingsCommandHandler(ITradeSettingsRepository settings, IExchangeAccountRepository accounts, TimeProvider time)
    : IRequestHandler<SaveTradeSettingsCommand, TradeSettings>
{
    /// <exception cref="TradeSettingsRequestException">A field is refused, or the default account is not there.</exception>
    public async Task<TradeSettings> Handle(SaveTradeSettingsCommand request, CancellationToken cancellationToken)
    {
        List<SettingsProblem> problems = [.. TradeSettingsRules.Check(request.Settings)];
        if (request.Settings.DefaultAccountId is { } account && await accounts.FindAsync(account, cancellationToken) is null)
        {
            problems.Insert(0, new SettingsProblem("defaultAccountId", "NoAccount", "такого аккаунта нет"));
        }

        if (problems.Count > 0)
        {
            throw new TradeSettingsRequestException(problems);
        }

        DateTimeOffset now = time.GetUtcNow();
        TradeSettings saved = request.Settings with { SavedAt = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMicrosecond)) };
        await settings.SaveAsync(saved, cancellationToken);
        return saved;
    }
}
