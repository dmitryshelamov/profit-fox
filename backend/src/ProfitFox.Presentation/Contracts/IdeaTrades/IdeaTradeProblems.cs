using Microsoft.AspNetCore.Mvc;

using ProfitFox.Application.IdeaTrades;
using ProfitFox.Presentation.Contracts.Strategies;

namespace ProfitFox.Presentation.Contracts.IdeaTrades;

/// <summary>Answers of the endpoints of trades by ideas to application exceptions: those of the strategy endpoints, and a refused entry with its reasons.</summary>
public static class IdeaTradeProblems
{
    /// <summary>The answer to a known exception, or <c>null</c> to let the exception through.</summary>
    public static IActionResult? Of(Exception exception)
    {
        if (exception is IdeaTradeRefusedException refused)
        {
            var problem = new ProblemDetails { Status = StatusCodes.Status409Conflict, Detail = refused.Message };
            problem.Extensions["reasons"] = refused.Reasons.Select(IdeaTradeCodes.ToCode).ToList();

            return new ConflictObjectResult(problem);
        }

        return StrategyProblems.Of(exception);
    }
}