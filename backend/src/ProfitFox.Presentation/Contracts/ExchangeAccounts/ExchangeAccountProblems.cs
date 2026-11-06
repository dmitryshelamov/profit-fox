using Microsoft.AspNetCore.Mvc;

using ProfitFox.Application.Exceptions;
using ProfitFox.Presentation.Contracts.Strategies;

namespace ProfitFox.Presentation.Contracts.ExchangeAccounts;

/// <summary>Answers of the exchange account endpoints to application exceptions.</summary>
public static class ExchangeAccountProblems
{
    /// <summary>The answer to a known exception, or <c>null</c> to let the exception through.</summary>
    public static IActionResult? Of(Exception exception)
    {
        switch (exception)
        {
            case ExchangeAccountInUseConflictException inUse:
                var problem = new ProblemDetails { Status = StatusCodes.Status409Conflict, Detail = inUse.Message };
                problem.Extensions["botNames"] = inUse.BotNames;
                problem.Extensions["autoNames"] = inUse.AutoNames;

                return new ConflictObjectResult(problem);
            case ConflictException conflict:
                return new ConflictObjectResult(new ProblemDetails { Status = StatusCodes.Status409Conflict, Detail = conflict.Message });
            case ExchangeException refused when refused is not ExchangeUnavailableException:
                // Bybit answered and refused: a retry will not help, unlike the 503 of an exchange that did not answer.
                return new ObjectResult(new ProblemDetails { Status = StatusCodes.Status502BadGateway, Detail = refused.Message })
                {
                    StatusCode = StatusCodes.Status502BadGateway
                };
            default:
                return StrategyProblems.Of(exception);
        }
    }
}