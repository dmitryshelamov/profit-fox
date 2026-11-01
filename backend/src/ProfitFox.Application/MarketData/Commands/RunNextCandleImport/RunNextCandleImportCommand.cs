using MediatR;

namespace ProfitFox.Application.MarketData.Commands.RunNextCandleImport;

/// <summary>Takes the next import job no Worker holds and runs it; returns whether there was one.</summary>
public sealed record RunNextCandleImportCommand : IRequest<bool>;