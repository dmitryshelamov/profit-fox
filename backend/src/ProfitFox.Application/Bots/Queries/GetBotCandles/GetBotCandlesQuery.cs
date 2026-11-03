using MediatR;

namespace ProfitFox.Application.Bots.Queries.GetBotCandles;

/// <summary>Raw parameters as the user sent them; <see cref="Interval" /> is the interval of the bot when <c>null</c>. The result is <c>null</c> for an unknown bot.</summary>
public sealed record GetBotCandlesQuery(Guid BotId, string? Interval, string From, string To) : IRequest<GetBotCandlesResult?>;