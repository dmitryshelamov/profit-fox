using MediatR;

using ProfitFox.Domain.Services.Strategies.Templates;

namespace ProfitFox.Application.Strategies.Queries.GetStrategyTemplates;

public sealed class GetStrategyTemplatesQueryHandler : IRequestHandler<GetStrategyTemplatesQuery, IReadOnlyList<StrategyTemplateSummary>>
{
    public Task<IReadOnlyList<StrategyTemplateSummary>> Handle(GetStrategyTemplatesQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyList<StrategyTemplateSummary> templates =
        [
            .. BuiltInTemplates.All.Select(template => new StrategyTemplateSummary(StrategyCodes.ToCode(template.Kind), template.Name, template.Description, template.Definition.Direction))
        ];

        return Task.FromResult(templates);
    }
}