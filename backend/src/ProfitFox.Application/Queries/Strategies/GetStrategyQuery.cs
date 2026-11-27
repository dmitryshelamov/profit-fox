using MediatR;

using ProfitFox.Application.Contracts.Strategies;

namespace ProfitFox.Application.Queries.Strategies;

/// <summary>
///     The latest version of the saved strategy <see cref="Name" />, with what its signal nodes point at now.
/// </summary>
public sealed record GetStrategyQuery(string Name) : IRequest<StrategyInternal>;
