using MediatR;

namespace ProfitFox.Application.Commands.Filters.DeleteFilter;

/// <summary>
///     Deletes a saved filter; false when there was none.
/// </summary>
public sealed record DeleteFilterCommand(Guid Id) : IRequest<bool>;

internal sealed class DeleteFilterCommandHandler(Domain.Repositories.ISavedFilterRepository filters) : IRequestHandler<DeleteFilterCommand, bool>
{
    public Task<bool> Handle(DeleteFilterCommand request, CancellationToken cancellationToken)
    {
        return filters.DeleteAsync(request.Id, cancellationToken);
    }
}
