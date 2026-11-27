using System.Text.Json;

using MediatR;

using ProfitFox.Application.Contracts.Preview;

namespace ProfitFox.Application.Queries.Preview;

/// <summary>
///     The preview of a signal on one pair over exploration (spec signal-preview), asked as the request gave it: the symbol of a
///     pair, a timeframe (<c>5m</c>, <c>15m</c>, <c>1h</c>, <c>4h</c>, <c>1d</c>) and the signal graph in the format of spec strategy-graph. Nothing
///     is checked yet.
/// </summary>
public sealed record PreviewSignalQuery(string? Instrument, string? Timeframe, JsonElement? Signal) : IRequest<PreviewOutcomeInternal>;
