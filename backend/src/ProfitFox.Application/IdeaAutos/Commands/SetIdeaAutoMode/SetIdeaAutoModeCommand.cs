using MediatR;

namespace ProfitFox.Application.IdeaAutos.Commands.SetIdeaAutoMode;

/// <summary>Changes the mode of an auto at once, with no question; <c>off</c>, <c>paper</c> or <c>live</c>.</summary>
public sealed record SetIdeaAutoModeCommand(Guid Id, string Mode) : IRequest;