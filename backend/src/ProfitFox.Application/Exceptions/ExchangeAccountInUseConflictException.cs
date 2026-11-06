namespace ProfitFox.Application.Exceptions;

/// <summary>
///     An exchange account cannot be deleted while bots trade with it or autos that are not deleted enter on it; <see cref="BotNames" /> and
///     <see cref="AutoNames" /> are those bots and autos.
/// </summary>
public sealed class ExchangeAccountInUseConflictException(IReadOnlyList<string> botNames, IReadOnlyList<string>? autoNames = null)
    : BaseApplicationException("Bots or autos trade with the exchange account.")
{
    public IReadOnlyList<string> BotNames { get; } = botNames;

    public IReadOnlyList<string> AutoNames { get; } = autoNames ?? [];
}