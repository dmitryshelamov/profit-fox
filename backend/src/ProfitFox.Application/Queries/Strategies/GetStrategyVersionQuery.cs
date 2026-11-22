using MediatR;

using ProfitFox.Application.Contracts.Strategies;

namespace ProfitFox.Application.Queries.Strategies;

/// <summary>
///     The version <see cref="Version" /> of the saved strategy <see cref="Name" /> (change add-strategy-backtest, design.md,
///     decision 9): what the form of a backtest shows of the version it is of.
/// </summary>
public sealed record GetStrategyVersionQuery(string Name, int Version) : IRequest<StrategyInternal>;
