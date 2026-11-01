using ProfitFox.Domain.ValueObjects.Bots;

namespace ProfitFox.Domain.Exceptions;

/// <summary>A bot fails validation against the latest version of its strategy; <see cref="Errors" /> lists every problem.</summary>
public sealed class BotValidationException(IReadOnlyList<BotError> errors) : Exception("The bot is invalid.")
{
    public IReadOnlyList<BotError> Errors { get; } = errors;
}