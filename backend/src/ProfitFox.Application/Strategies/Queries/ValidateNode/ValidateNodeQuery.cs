using System.Text.Json;

using MediatR;

namespace ProfitFox.Application.Strategies.Queries.ValidateNode;

/// <summary>What saving the composite node would reject: a new one when <paramref name="Id" /> is <c>null</c>, else a change of that node.</summary>
public sealed record ValidateNodeQuery(string? Id, string Name, JsonElement Definition) : IRequest<ValidationReport>;