using System.Text.Json;

using MediatR;

namespace ProfitFox.Application.Strategies.Queries.ValidateStrategy;

/// <summary>What saving the strategy would reject: a new one when <paramref name="Id" /> is <c>null</c>, else a change of that strategy.</summary>
public sealed record ValidateStrategyQuery(Guid? Id, string Name, JsonElement Definition) : IRequest<ValidationReport>;