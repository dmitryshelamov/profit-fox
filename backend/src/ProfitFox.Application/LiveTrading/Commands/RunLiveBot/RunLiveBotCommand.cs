using MediatR;

using ProfitFox.Domain.ValueObjects.LiveTrading;

namespace ProfitFox.Application.LiveTrading.Commands.RunLiveBot;

/// <summary>Keeps a taken bot until it is done, its lease is lost or the Worker stops.</summary>
public sealed record RunLiveBotCommand(LeasedBot Bot) : IRequest;