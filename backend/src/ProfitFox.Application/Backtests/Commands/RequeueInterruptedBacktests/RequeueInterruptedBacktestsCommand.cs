using MediatR;

namespace ProfitFox.Application.Backtests.Commands.RequeueInterruptedBacktests;

/// <summary>Runs at startup: backtests the previous process left unfinished go back to the queue.</summary>
public sealed record RequeueInterruptedBacktestsCommand : IRequest;