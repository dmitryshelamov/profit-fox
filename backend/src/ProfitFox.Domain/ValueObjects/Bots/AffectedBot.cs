namespace ProfitFox.Domain.ValueObjects.Bots;

/// <summary>A bot that stands in the way of a strategy change, with the errors the change would cause in it.</summary>
public sealed record AffectedBot(Guid Id, string Name, IReadOnlyList<BotError> Errors);