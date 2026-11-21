using MediatR;

using ProfitFox.Application.Commands.Signals.Base;
using ProfitFox.Application.Contracts.Signals;
using ProfitFox.Application.Contracts.Signals.Converters;
using ProfitFox.Domain.Entities.Signals;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.Queries.Signals;

internal sealed class GetSignalsQueryHandler(ISignalRepository signals) : IRequestHandler<GetSignalsQuery, IReadOnlyList<SignalEntryInternal>>
{
    public async Task<IReadOnlyList<SignalEntryInternal>> Handle(GetSignalsQuery request, CancellationToken cancellationToken)
    {
        List<SignalEntryInternal> entries = [];
        foreach (string name in (await signals.NamesAsync(cancellationToken)).Order(StringComparer.Ordinal))
        {
            entries.Add(await Entry(name, cancellationToken));
        }

        return entries;
    }

    private async Task<SignalEntryInternal> Entry(string name, CancellationToken cancellationToken)
    {
        try
        {
            SignalDocument signal = await SignalChecks.ReadAsync(signals, name, cancellationToken);
            return signal.ToEntryInternal();
        }
        catch (SignalLibraryException exception)
        {
            return new SignalEntryInternal(name, Description: null, Version: 0, exception.Message);
        }
    }
}
