using MediatR;

namespace ProfitFox.Application.IdeaAutos.Commands.SaveIdeaAuto;

/// <summary>Writes the name, the account, the amount, the budget and the conditions of an auto; its mode and place stay.</summary>
public sealed record SaveIdeaAutoCommand(Guid Id, IdeaAutoInput Input) : IRequest;