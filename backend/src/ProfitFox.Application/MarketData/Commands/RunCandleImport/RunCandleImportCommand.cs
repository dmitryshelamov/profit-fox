using MediatR;

namespace ProfitFox.Application.MarketData.Commands.RunCandleImport;

public sealed record RunCandleImportCommand(Guid JobId) : IRequest;