using System.Text.Json;

using MediatR;

using ProfitFox.Application.Contracts.Preview;

namespace ProfitFox.Application.Queries.Preview;

/// <summary>
///     The preview of a strategy on one pair over exploration (spec strategy-preview), asked as the request gave it: the
///     symbol of a pair, a timeframe (<c>1h</c>, <c>4h</c>, <c>1d</c>) and the graph of the strategy as written, with its
///     signal nodes. Nothing is checked yet.
/// </summary>
public sealed record PreviewStrategyQuery(string? Instrument, string? Timeframe, JsonElement? Strategy) : IRequest<StrategyPreviewOutcomeInternal>;
