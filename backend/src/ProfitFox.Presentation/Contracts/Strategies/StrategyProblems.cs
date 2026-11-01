using Microsoft.AspNetCore.Mvc;

using ProfitFox.Application.Exceptions;

namespace ProfitFox.Presentation.Contracts.Strategies;

/// <summary>Answers of the strategy, node and bot endpoints to application exceptions.</summary>
public static class StrategyProblems
{
    /// <summary>The answer to a known exception, or <c>null</c> to let the exception through.</summary>
    public static IActionResult? Of(Exception exception)
    {
        switch (exception)
        {
            case ValidationFailedException failed:
                return new BadRequestObjectResult(
                    new ValidationProblemDetails(failed.Errors.ToDictionary(error => error.Key, error => new[] { error.Value })) { Status = StatusCodes.Status400BadRequest });
            case NotFoundException notFound:
                return new NotFoundObjectResult(new ProblemDetails { Status = StatusCodes.Status404NotFound, Detail = notFound.Message });
            case StateConflictException conflict:
                var problem = new ProblemDetails { Status = StatusCodes.Status409Conflict, Detail = conflict.Message };
                problem.Extensions["usages"] = conflict.Usages.Select(UsageResponse.Of);

                if (conflict.Bots is { } bots)
                {
                    problem.Extensions["bots"] = bots.Select(AffectedBotResponse.Of);
                }

                return new ConflictObjectResult(problem);
            case ExchangeException unavailable:
                return new ObjectResult(new ProblemDetails { Status = StatusCodes.Status503ServiceUnavailable, Detail = unavailable.Message })
                {
                    StatusCode = StatusCodes.Status503ServiceUnavailable
                };
            default:
                return null;
        }
    }
}