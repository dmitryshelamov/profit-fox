using MediatR;

using ProfitFox.Application.Exceptions;
using ProfitFox.Domain.Entities.Backtests.Enums;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.Backtests.Queries.GetBacktestResults;

public sealed class GetBacktestResultsQueryHandler(IBacktestRepository backtests) : IRequestHandler<GetBacktestResultsQuery, GetBacktestResultsResult?>
{
    private const int DefaultLimit = 100;
    private const int MaxLimit = 1000;

    /// <summary>Results can be read while the backtest is still running and after a cancel.</summary>
    /// <exception cref="ValidationFailedException">The metric, the order or the page is invalid.</exception>
    public async Task<GetBacktestResultsResult?> Handle(GetBacktestResultsQuery request, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string>();
        ResultMetric sort = ResultMetric.Combination;

        if (request.Sort is not null && !BacktestCodes.TryParseMetric(request.Sort, out sort))
        {
            errors["sort"] = $"There is no metric {request.Sort}.";
        }

        if (request.Order is not (null or "asc" or "desc"))
        {
            errors["order"] = "Order must be asc or desc.";
        }

        int offset = request.Offset ?? 0;
        int limit = request.Limit ?? DefaultLimit;

        if (offset < 0)
        {
            errors["offset"] = "Offset must not be negative.";
        }

        if (limit is < 1 or > MaxLimit)
        {
            errors["limit"] = $"Limit must be from 1 to {MaxLimit}.";
        }

        if (errors.Count > 0)
        {
            throw new ValidationFailedException(errors);
        }

        if (await backtests.GetAsync(request.BacktestId, cancellationToken) is null)
        {
            return null;
        }

        return new GetBacktestResultsResult(
            await backtests.CountResultsAsync(request.BacktestId, cancellationToken),
            await backtests.GetResultsAsync(request.BacktestId, sort, request.Order == "desc", offset, limit, cancellationToken));
    }
}