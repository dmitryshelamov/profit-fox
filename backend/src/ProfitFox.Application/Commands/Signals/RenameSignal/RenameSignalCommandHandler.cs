using MediatR;

using ProfitFox.Application.Commands.Signals.Base;
using ProfitFox.Application.Contracts.Signals;
using ProfitFox.Application.Contracts.Signals.Converters;
using ProfitFox.Domain.Entities.Signals;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Signals;

namespace ProfitFox.Application.Commands.Signals.RenameSignal;

internal sealed class RenameSignalCommandHandler(ISignalRepository signals) : IRequestHandler<RenameSignalCommand, SignalInternal>
{
    /// <exception cref="SignalLibraryException">There is no such signal, or the new name is not allowed or is taken.</exception>
    public async Task<SignalInternal> Handle(RenameSignalCommand request, CancellationToken cancellationToken)
    {
        SignalDocument signal = await SignalChecks.ReadAsync(signals, request.Name, cancellationToken);
        if (!SignalNames.IsValid(request.To))
        {
            throw new SignalLibraryException(SignalProblem.Invalid, $"{request.To} is not a signal name", [SignalChecks.NameError(request.To)]);
        }

        if (request.To == request.Name)
        {
            return signal.ToInternal();
        }

        if (await signals.ExistsAsync(request.To, cancellationToken))
        {
            throw SignalChecks.Taken(request.To);
        }

        await signals.RenameAsync(request.Name, request.To, cancellationToken);
        signal.Rename(request.To);
        return signal.ToInternal();
    }
}
