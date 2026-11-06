using MediatR;

namespace ProfitFox.Application.IdeaAutos.Commands.DeleteIdeaAuto;

/// <summary>Takes an auto off the list. Its trades stay: the open ones go on to their outcome and carry its name.</summary>
public sealed record DeleteIdeaAutoCommand(Guid Id) : IRequest;