using MediatR;

using ProfitFox.Application.Services.Ideas;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.Queries.Ideas;

/// <summary>
///     The lease of the scanner on the time of the database: live while it holds; nobody keeps the live data for as long as
///     it ran out, and for ever when nobody ever took it.
/// </summary>
internal sealed class GetScannerStateQueryHandler(IScannerLeaseRepository lease, IIdeaRepository ideas, IdeasSettings settings)
    : IRequestHandler<GetScannerStateQuery, ScannerStateInternal>
{
    public async Task<ScannerStateInternal> Handle(GetScannerStateQuery request, CancellationToken cancellationToken)
    {
        (int open, int closed) = await ideas.CountsAsync(cancellationToken);
        if (await lease.StateAsync(cancellationToken) is not ({ } state, DateTimeOffset now))
        {
            return new ScannerStateInternal(null, Live: false, Idle: null, LiveSince: null, 0, 0, open, closed, settings.Burst);
        }

        bool live = state.Until > now;
        return new ScannerStateInternal(
            state.Worker,
            live,
            live ? TimeSpan.Zero : now - state.Until,
            live ? state.LiveSince : null,
            state.Warmed,
            state.Pairs,
            open,
            closed,
            settings.Burst);
    }
}
