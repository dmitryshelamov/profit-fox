using MediatR;

using ProfitFox.Application.IdeaTrades.Runtime;
using ProfitFox.Application.Ports.LiveTrading;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Application.Ports.Security;
using ProfitFox.Application.Transactions;
using ProfitFox.Application.Workers;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.IdeaTrades.Commands.RunIdeaTrade;

public sealed class RunIdeaTradeCommandHandler(
    IIdeaTradeRepository repository,
    ILiveBotRepository bots,
    IStrategyLibraryRepository library,
    IExchangeAccountRepository accounts,
    ISecretProtector protector,
    IExchangeLedger ledger,
    IOrderGateway gateway,
    IExchangeStreams streams,
    IInstrumentRulesProvider rules,
    IInstrumentCatalog catalog,
    ITransactionExecutor executor,
    ILiveTradingJournal journal,
    IdeaTradeLeases leases,
    WorkerSettings settings,
    WorkerIdentity worker,
    TimeProvider time) : IRequestHandler<RunIdeaTradeCommand>
{
    public Task Handle(RunIdeaTradeCommand request, CancellationToken cancellationToken)
    {
        var session = new IdeaTradeSession(request.Trade, repository, bots, library, accounts, protector, ledger, gateway, streams, rules, catalog, executor, journal, leases, settings, worker, time);

        return session.RunAsync(cancellationToken);
    }
}