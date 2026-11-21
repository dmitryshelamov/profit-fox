using MediatR;

using ProfitFox.Application.Commands.Signals.Base;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Signals;

namespace ProfitFox.Application.Commands.Signals.DeleteSignal;

internal sealed class DeleteSignalCommandHandler(ISignalRepository signals) : IRequestHandler<DeleteSignalCommand>
{
    /// <exception cref="SignalLibraryException">There is no such signal.</exception>
    public async Task Handle(DeleteSignalCommand request, CancellationToken cancellationToken)
    {
        if (!SignalNames.IsValid(request.Name) || !await signals.ExistsAsync(request.Name, cancellationToken))
        {
            throw SignalChecks.NotFound(request.Name);
        }

        await signals.DeleteAsync(request.Name, cancellationToken);
    }
}
