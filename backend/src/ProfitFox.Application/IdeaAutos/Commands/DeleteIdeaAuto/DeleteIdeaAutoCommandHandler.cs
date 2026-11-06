using MediatR;

using ProfitFox.Application.Exceptions;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.IdeaAutos.Commands.DeleteIdeaAuto;

public sealed class DeleteIdeaAutoCommandHandler(IIdeaAutoRepository autos, TimeProvider time) : IRequestHandler<DeleteIdeaAutoCommand>
{
    /// <exception cref="NotFoundException">There is no such auto.</exception>
    public async Task Handle(DeleteIdeaAutoCommand request, CancellationToken cancellationToken)
    {
        if (!await autos.DeleteAsync(request.Id, time.GetUtcNow(), cancellationToken))
        {
            throw new NotFoundException($"There is no auto {request.Id}.");
        }
    }
}