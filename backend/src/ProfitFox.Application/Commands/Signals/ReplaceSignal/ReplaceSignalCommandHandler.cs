using MediatR;

using ProfitFox.Application.Commands.Signals.Base;
using ProfitFox.Application.Contracts.Signals;
using ProfitFox.Application.Contracts.Signals.Converters;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Signals;

namespace ProfitFox.Application.Commands.Signals.ReplaceSignal;

internal sealed class ReplaceSignalCommandHandler(ISignalRepository signals) : IRequestHandler<ReplaceSignalCommand, SignalInternal>
{
    /// <exception cref="SignalLibraryException">There is no such signal, or the graph is refused.</exception>
    public async Task<SignalInternal> Handle(ReplaceSignalCommand request, CancellationToken cancellationToken)
    {
        if (!SignalNames.IsValid(request.Name) || !await signals.ExistsAsync(request.Name, cancellationToken))
        {
            throw SignalChecks.NotFound(request.Name);
        }

        return (await signals.ReplaceAsync(SignalChecks.Checked(request.Name, request.Description, request.Graph, request.Layout), cancellationToken)).ToInternal();
    }
}
