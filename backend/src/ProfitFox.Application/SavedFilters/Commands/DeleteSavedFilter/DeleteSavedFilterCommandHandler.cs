using MediatR;

using ProfitFox.Application.Exceptions;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.SavedFilters.Commands.DeleteSavedFilter;

public sealed class DeleteSavedFilterCommandHandler(ISavedFilterRepository filters) : IRequestHandler<DeleteSavedFilterCommand>
{
    /// <exception cref="NotFoundException">There is no such filter.</exception>
    public async Task Handle(DeleteSavedFilterCommand request, CancellationToken cancellationToken)
    {
        if (!await filters.DeleteAsync(request.Id, cancellationToken))
        {
            throw new NotFoundException($"Saved filter {request.Id} not found.");
        }
    }
}