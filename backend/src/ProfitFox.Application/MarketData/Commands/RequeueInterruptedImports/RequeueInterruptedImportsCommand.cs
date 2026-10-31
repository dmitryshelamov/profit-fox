using MediatR;

namespace ProfitFox.Application.MarketData.Commands.RequeueInterruptedImports;

/// <summary>Runs at startup: jobs left <c>Pending</c> or <c>Running</c> by the previous process go back to the queue.</summary>
public sealed record RequeueInterruptedImportsCommand : IRequest;