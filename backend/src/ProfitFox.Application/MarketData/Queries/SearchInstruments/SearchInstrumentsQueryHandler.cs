using MediatR;

using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.MarketData.Queries.SearchInstruments;

public sealed class SearchInstrumentsQueryHandler(IInstrumentCatalog catalog) : IRequestHandler<SearchInstrumentsQuery, SearchInstrumentsResult>
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 100;

    public async Task<SearchInstrumentsResult> Handle(SearchInstrumentsQuery request, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string>();

        if (!MarketDataCodes.TryParseCategory(request.Category, out MarketCategory category))
        {
            errors["category"] = MarketDataCodes.InvalidCategory;
        }

        if (string.IsNullOrWhiteSpace(request.Query))
        {
            errors["query"] = "Query must not be empty.";
        }

        int limit = request.Limit ?? DefaultLimit;

        if (limit is < 1 or > MaxLimit)
        {
            errors["limit"] = $"limit must be from 1 to {MaxLimit}.";
        }

        if (errors.Count > 0)
        {
            return new SearchInstrumentsResult(Instruments: null, errors);
        }

        IReadOnlyList<ListedInstrument> listed = await catalog.ListAsync(category, cancellationToken);

        return new SearchInstrumentsResult(InstrumentSearch.Find(listed, request.Query.Trim(), limit), errors);
    }
}