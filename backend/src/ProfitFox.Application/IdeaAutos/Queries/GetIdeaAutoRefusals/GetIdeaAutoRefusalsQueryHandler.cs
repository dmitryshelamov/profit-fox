using MediatR;

using ProfitFox.Application.Paging;
using ProfitFox.Domain.Entities.IdeaAutos;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.IdeaAutos.Queries.GetIdeaAutoRefusals;

public sealed class GetIdeaAutoRefusalsQueryHandler(IIdeaAutoRefusalRepository refusals, IIdeaAutoRepository autos, TimeProvider time) : IRequestHandler<GetIdeaAutoRefusalsQuery, GetIdeaAutoRefusalsResult>
{
    public const string InvalidPeriod = "Period must be today or 7d.";
    public const string InvalidAuto = "There is no such auto.";

    public async Task<GetIdeaAutoRefusalsResult> Handle(GetIdeaAutoRefusalsQuery request, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string>();
        DateTimeOffset now = time.GetUtcNow();
        DateTimeOffset since = default;

        switch (request.Period)
        {
            case "today":
                since = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);

                break;
            case "7d":
                since = now - TimeSpan.FromDays(7);

                break;
            default:
                errors["period"] = InvalidPeriod;

                break;
        }

        IReadOnlyDictionary<Guid, IdeaAuto> every = await autos.GetEveryAsync(cancellationToken);

        if (request.AutoId is { } autoId && !every.ContainsKey(autoId))
        {
            errors["autoId"] = InvalidAuto;
        }

        var page = PageRequest.Parse(request.Page, request.PageSize, errors);

        if (errors.Count > 0)
        {
            return new GetIdeaAutoRefusalsResult(null, errors);
        }

        (IReadOnlyList<IdeaAutoRefusal> items, int total) = await refusals.GetPageAsync(since, request.AutoId, (page.Number - 1) * page.Size, page.Size, cancellationToken);
        int number = page.Number;

        // A number past the end gives the last page.
        if (items.Count == 0 && total > 0)
        {
            number = (total + page.Size - 1) / page.Size;
            (items, _) = await refusals.GetPageAsync(since, request.AutoId, (number - 1) * page.Size, page.Size, cancellationToken);
        }

        return new GetIdeaAutoRefusalsResult(new Page<IdeaAutoRefusalView>([.. items.Select(refusal => new IdeaAutoRefusalView(refusal, every[refusal.AutoId].Name))], number, page.Size, total), errors);
    }
}