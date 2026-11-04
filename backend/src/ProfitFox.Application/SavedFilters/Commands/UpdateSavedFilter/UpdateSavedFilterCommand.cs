using MediatR;

namespace ProfitFox.Application.SavedFilters.Commands.UpdateSavedFilter;

/// <summary>Replaces the name, place and conditions of a saved filter.</summary>
public sealed record UpdateSavedFilterCommand(Guid Id, SavedFilterInput Filter) : IRequest;