using ProfitFox.Application.Bots;
using ProfitFox.Domain.ValueObjects.Bots;

namespace ProfitFox.Presentation.Contracts.Strategies;

/// <param name="Id"></param>
/// <param name="Name"></param>
/// <param name="Errors">The errors a strategy change would cause in the bot, keyed by its field.</param>
public sealed record AffectedBotResponse(Guid Id, string Name, IReadOnlyDictionary<string, string> Errors)
{
    public static AffectedBotResponse Of(AffectedBot bot)
    {
        return new AffectedBotResponse(bot.Id, bot.Name, BotRequests.Errors(bot.Errors));
    }
}