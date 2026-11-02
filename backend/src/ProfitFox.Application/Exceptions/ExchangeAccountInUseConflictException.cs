namespace ProfitFox.Application.Exceptions;

/// <summary>An exchange account cannot be deleted while bots trade with it; <see cref="BotNames" /> are those bots.</summary>
public sealed class ExchangeAccountInUseConflictException(IReadOnlyList<string> botNames)
    : BaseApplicationException("Bots trade with the exchange account.")
{
    public IReadOnlyList<string> BotNames { get; } = botNames;
}