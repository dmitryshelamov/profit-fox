using MediatR;

using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.IdeaAutos.Queries.GetIdeaAuto;

public sealed class GetIdeaAutoQueryHandler(IIdeaAutoRepository autos, IExchangeAccountRepository accounts) : IRequestHandler<GetIdeaAutoQuery, IdeaAutoSummary?>
{
    public async Task<IdeaAutoSummary?> Handle(GetIdeaAutoQuery request, CancellationToken cancellationToken)
    {
        if (await autos.GetAsync(request.Id, cancellationToken) is not { DeletedAt: null } auto)
        {
            return null;
        }

        return new IdeaAutoSummary(auto, (await accounts.GetAsync(auto.AccountId, cancellationToken))!.Name, 0, null, 0m, 0, 0m, 0, 0m);
    }
}