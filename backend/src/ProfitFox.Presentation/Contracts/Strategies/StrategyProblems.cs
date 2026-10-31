using Microsoft.AspNetCore.Mvc;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Strategies;

namespace ProfitFox.Presentation.Contracts.Strategies;

/// <summary>Answers of the strategy endpoints to application exceptions.</summary>
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
                problem.Extensions["usages"] = conflict.Usages.Select(usage => new
                {
                    kind = StrategyCodes.ToCode(usage.Kind),
                    id = usage.Id,
                    name = usage.Name,
                    errors = StrategyRequests.Errors(usage.Errors)
                });

                return new ConflictObjectResult(problem);
            default:
                return null;
        }
    }
}