using MediatR;

using ProfitFox.Application.LiveTrading.Runtime;
using ProfitFox.Application.Ports.LiveTrading;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Application.Ports.Security;
using ProfitFox.Application.Transactions;
using ProfitFox.Application.Workers;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.LiveTrading.Commands.RunLiveBot;

public sealed class RunLiveBotCommandHandler(
    ILiveBotRepository repository,
    IIdeaTradeRepository ideaTrades,
    IStrategyLibraryRepository library,
    IExchangeAccountRepository accounts,
    ISecretProtector protector,
    IExchangeLedger ledger,
    IOrderGateway gateway,
    IExchangeStreams streams,
    IMarketHistoryClient history,
    IInstrumentRulesProvider rules,
    IInstrumentCatalog catalog,
    ITransactionExecutor executor,
    ILiveTradingJournal journal,
    LiveBotLeases leases,
    WorkerSettings settings,
    WorkerIdentity worker,
    TimeProvider time) : IRequestHandler<RunLiveBotCommand>
{
    public Task Handle(RunLiveBotCommand request, CancellationToken cancellationToken)
    {
        var session = new LiveBotSession(
            request.Bot,
            repository,
            ideaTrades,
            library,
            accounts,
            protector,
            ledger,
            gateway,
            streams,
            history,
            rules,
            catalog,
            executor,
            journal,
            leases,
            settings,
            worker,
            time);

        return session.RunAsync(cancellationToken);
    }
}