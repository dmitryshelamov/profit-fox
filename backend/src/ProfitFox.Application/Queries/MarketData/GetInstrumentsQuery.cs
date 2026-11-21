using MediatR;

using ProfitFox.Application.Contracts.MarketData;

namespace ProfitFox.Application.Queries.MarketData;

/// <summary>
///     The pairs of a bench run (spec signal-preview «Список пар»), ordered by symbol.
/// </summary>
public sealed record GetInstrumentsQuery : IRequest<IReadOnlyList<InstrumentInternal>>;
