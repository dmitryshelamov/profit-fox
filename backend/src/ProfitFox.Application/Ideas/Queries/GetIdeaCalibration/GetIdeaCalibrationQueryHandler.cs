using MediatR;

using ProfitFox.Application.MarketData;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Ideas;

namespace ProfitFox.Application.Ideas.Queries.GetIdeaCalibration;

public sealed class GetIdeaCalibrationQueryHandler(IIdeaCalibrationRepository calibrations, IdeasEngineVersion version)
    : IRequestHandler<GetIdeaCalibrationQuery, GetIdeaCalibrationResult>
{
    public async Task<GetIdeaCalibrationResult> Handle(GetIdeaCalibrationQuery request, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string>();
        int days = request.Days switch
        {
            null or "90" => 90,
            "30" => 30,
            _ => 0
        };
        MarketCategory? category = null;

        if (days == 0)
        {
            errors["days"] = "Days must be 90 or 30.";
        }

        if (request.Category is not null)
        {
            if (MarketDataCodes.TryParseCategory(request.Category, out MarketCategory parsed))
            {
                category = parsed;
            }
            else
            {
                errors["category"] = "Category must be linear or spot.";
            }
        }

        if (errors.Count > 0)
        {
            return new GetIdeaCalibrationResult(days, category, null, errors);
        }

        return new GetIdeaCalibrationResult(days, category, await calibrations.GetAsync(version.Number, days, category, CalibrationScope.All, cancellationToken), errors);
    }
}