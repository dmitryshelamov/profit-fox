using MediatR;

namespace ProfitFox.Application.MarketData.Commands.RetryCandleImport;

public sealed record RetryCandleImportCommand(Guid JobId) : IRequest;