using System.Text.Json;

using MediatR;

namespace ProfitFox.Application.Bots.Commands.CreateBot;

/// <summary>A new bot from raw settings as the user sent them; returns its id.</summary>
public sealed record CreateBotCommand(
    string Name,
    Guid StrategyId,
    JsonElement? ParameterValues,
    string Category,
    string Symbol,
    string Interval,
    decimal Balance) : IRequest<Guid>;