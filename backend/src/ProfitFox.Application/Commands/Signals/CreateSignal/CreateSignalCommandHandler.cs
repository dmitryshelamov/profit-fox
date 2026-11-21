using MediatR;

using ProfitFox.Application.Commands.Signals.Base;
using ProfitFox.Application.Contracts.Signals;
using ProfitFox.Application.Contracts.Signals.Converters;
using ProfitFox.Domain.Entities.Signals;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.Commands.Signals.CreateSignal;

internal sealed class CreateSignalCommandHandler(ISignalRepository signals) : IRequestHandler<CreateSignalCommand, SignalInternal>
{
    /// <exception cref="SignalLibraryException">The name or the graph is refused, or the name is taken.</exception>
    public async Task<SignalInternal> Handle(CreateSignalCommand request, CancellationToken cancellationToken)
    {
        SignalDocument signal = SignalChecks.Checked(request.Name, request.Description, request.Graph, request.Layout);
        if (await signals.ExistsAsync(request.Name, cancellationToken))
        {
            throw SignalChecks.Taken(request.Name);
        }

        return (await signals.CreateAsync(signal, cancellationToken)).ToInternal();
    }
}
