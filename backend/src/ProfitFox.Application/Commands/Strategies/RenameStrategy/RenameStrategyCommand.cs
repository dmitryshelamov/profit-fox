using MediatR;

using ProfitFox.Application.Contracts.Strategies;

namespace ProfitFox.Application.Commands.Strategies.RenameStrategy;

/// <summary>
///     Gives a saved strategy the name <see cref="To" /> and keeps its versions.
/// </summary>
public sealed record RenameStrategyCommand(string Name, string To) : IRequest<StrategyInternal>;
