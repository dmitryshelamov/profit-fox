using MediatR;

namespace ProfitFox.Application.Strategies.Commands.CreateMissingStrategyVersions;

/// <summary>Runs at startup: strategies saved before versions existed get version 1.</summary>
public sealed record CreateMissingStrategyVersionsCommand : IRequest;