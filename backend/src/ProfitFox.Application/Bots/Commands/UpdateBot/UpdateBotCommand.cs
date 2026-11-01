using System.Text.Json;

using MediatR;

namespace ProfitFox.Application.Bots.Commands.UpdateBot;

/// <summary>Replaces the settings of a bot; its strategy stays.</summary>
public sealed record UpdateBotCommand(
    Guid Id,
    string Name,
    JsonElement? ParameterValues,
    string Category,
    string Symbol,
    string Interval,
    decimal Balance) : IRequest;