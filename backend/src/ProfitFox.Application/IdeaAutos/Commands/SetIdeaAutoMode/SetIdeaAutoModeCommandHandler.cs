using MediatR;

using ProfitFox.Application.Exceptions;
using ProfitFox.Domain.Entities.IdeaAutos.Enums;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.IdeaAutos.Commands.SetIdeaAutoMode;

public sealed class SetIdeaAutoModeCommandHandler(IIdeaAutoRepository autos) : IRequestHandler<SetIdeaAutoModeCommand>
{
    /// <exception cref="ValidationFailedException">An unknown mode.</exception>
    /// <exception cref="NotFoundException">There is no such auto.</exception>
    public async Task Handle(SetIdeaAutoModeCommand request, CancellationToken cancellationToken)
    {
        if (!IdeaAutoCodes.TryParseMode(request.Mode, out IdeaAutoMode mode))
        {
            throw new ValidationFailedException(new Dictionary<string, string> { ["mode"] = "The mode must be off, paper or live." });
        }

        if (!await autos.SetModeAsync(request.Id, mode, cancellationToken))
        {
            throw new NotFoundException($"There is no auto {request.Id}.");
        }
    }
}