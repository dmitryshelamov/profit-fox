using MediatR;

namespace ProfitFox.Application.MarketData.Commands.FailInterruptedImports;

/// <summary>Runs at startup: jobs left <c>Pending</c> or <c>Running</c> by the previous process will never finish.</summary>
public sealed record FailInterruptedImportsCommand : IRequest;