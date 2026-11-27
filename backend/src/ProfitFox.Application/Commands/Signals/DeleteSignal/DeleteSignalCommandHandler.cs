using MediatR;

using ProfitFox.Application.Commands.Signals.Base;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Signals;

namespace ProfitFox.Application.Commands.Signals.DeleteSignal;

internal sealed class DeleteSignalCommandHandler(ISignalRepository signals, IStrategyRepository strategies) : IRequestHandler<DeleteSignalCommand>
{
    /// <exception cref="SignalLibraryException">
    ///     There is no such signal, or a version of a saved strategy points at it (spec signal-constructor «Сигнал, на который
    ///     ссылается стратегия»): <c>InUse</c> with the names of the strategies.
    /// </exception>
    public async Task Handle(DeleteSignalCommand request, CancellationToken cancellationToken)
    {
        if (!SignalNames.IsValid(request.Name) || await signals.ReadAsync(request.Name, cancellationToken) is not { } read)
        {
            throw SignalChecks.NotFound(request.Name);
        }

        // The strategies are asked first to answer with their names; a strategy saved after this question is refused by the
        // repository with the same answer.
        if (read.Signal is { } signal && await strategies.UsingSignalAsync(signal.Id, cancellationToken) is { Count: > 0 } names)
        {
            throw SignalChecks.InUse(request.Name, names);
        }

        await signals.DeleteAsync(request.Name, cancellationToken);
    }
}
