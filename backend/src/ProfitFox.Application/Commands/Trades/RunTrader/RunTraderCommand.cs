using MediatR;

using Microsoft.Extensions.Logging;

using ProfitFox.Application.Ports.Exchange;
using ProfitFox.Application.Ports.Security;
using ProfitFox.Application.Services.Trades;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.Commands.Trades.RunTrader;

/// <summary>
///     Leads the trades by ideas (change take-idea-trades, design.md, decisions 2 and 3) as the Worker <see cref="Worker" />
///     that holds the lease of the trader, until the request is cancelled — the Worker stops or loses the lease — or a write
///     finds the lease of another Worker. It ends with <c>true</c>.
/// </summary>
public sealed record RunTraderCommand(string Worker) : IRequest<bool>;

internal sealed class RunTraderCommandHandler(
    ITradeRepository trades,
    ITradeHaltRepository halt,
    ITradeSettingsRepository settings,
    IExchangeAccountRepository accounts,
    IExchangeTrading trading,
    IExchangeMarket market,
    IExchangeStream streams,
    IExchangePriceStream prices,
    ISecretProtector protector,
    TradingSettings options,
    TimeProvider time,
    ILogger<Trader> logger) : IRequestHandler<RunTraderCommand, bool>
{
    public async Task<bool> Handle(RunTraderCommand request, CancellationToken cancellationToken)
    {
        var trader = new Trader(trades, halt, settings, accounts, trading, market, streams, prices, protector, options, time, logger);
        await trader.RunAsync(request.Worker, cancellationToken);
        return true;
    }
}
