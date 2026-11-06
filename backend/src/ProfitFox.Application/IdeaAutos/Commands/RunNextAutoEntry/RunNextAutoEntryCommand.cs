using MediatR;

namespace ProfitFox.Application.IdeaAutos.Commands.RunNextAutoEntry;

/// <summary>Takes the earliest entry request of the autos with money and makes the entry or writes the refusal; returns whether there was a request to work on.</summary>
public sealed record RunNextAutoEntryCommand : IRequest<bool>;