using MediatR;

using ProfitFox.Application.Commands.Signals.Base;
using ProfitFox.Application.Contracts.Signals;
using ProfitFox.Application.Contracts.Signals.Converters;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.Queries.Signals;

internal sealed class GetSignalQueryHandler(ISignalRepository signals) : IRequestHandler<GetSignalQuery, SignalInternal>
{
    /// <exception cref="SignalLibraryException">There is no such signal, or it does not read.</exception>
    public async Task<SignalInternal> Handle(GetSignalQuery request, CancellationToken cancellationToken)
    {
        return (await SignalChecks.ReadAsync(signals, request.Name, cancellationToken)).ToInternal();
    }
}
