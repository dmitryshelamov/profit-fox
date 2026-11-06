using MediatR;

using ProfitFox.Domain.Services.Strategies.Templates;

namespace ProfitFox.Application.Strategies.Queries.GetStrategyTemplate;

public sealed class GetStrategyTemplateQueryHandler : IRequestHandler<GetStrategyTemplateQuery, StrategyTemplateResult?>
{
    public Task<StrategyTemplateResult?> Handle(GetStrategyTemplateQuery request, CancellationToken cancellationToken)
    {
        if (!StrategyCodes.TryParse(request.Code, out StrategyTemplateKind kind))
        {
            return Task.FromResult<StrategyTemplateResult?>(null);
        }

        StrategyTemplate template = BuiltInTemplates.All.Single(candidate => candidate.Kind == kind);

        return Task.FromResult<StrategyTemplateResult?>(new StrategyTemplateResult(request.Code, template.Name, template.Description, template.Definition));
    }
}