using MediatR;

namespace ProfitFox.Application.Commands.Strategies.DeleteStrategy;

/// <summary>
///     Removes a saved strategy with its versions; it must be there.
/// </summary>
public sealed record DeleteStrategyCommand(string Name) : IRequest;
