using MediatR;

using ProfitFox.Domain.Entities.IdeaTrades;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.IdeaTrades.Queries.GetIdeaTradingSettings;

public sealed class GetIdeaTradingSettingsQueryHandler(IIdeaTradingSettingsRepository repository) : IRequestHandler<GetIdeaTradingSettingsQuery, IdeaTradingSettings>
{
    public Task<IdeaTradingSettings> Handle(GetIdeaTradingSettingsQuery request, CancellationToken cancellationToken)
    {
        return repository.GetAsync(cancellationToken);
    }
}