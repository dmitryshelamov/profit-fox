using ProfitFox.Domain.Entities.Bots.Enums;

namespace ProfitFox.Domain.ValueObjects.Bots;

/// <param name="Field"></param>
/// <param name="Parameter">The strategy parameter of a <see cref="BotField.Parameter" /> error.</param>
/// <param name="Reason"></param>
public sealed record BotError(BotField Field, string? Parameter, string Reason);