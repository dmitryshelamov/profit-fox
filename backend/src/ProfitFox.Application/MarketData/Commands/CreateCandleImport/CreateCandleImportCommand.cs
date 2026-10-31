using MediatR;

namespace ProfitFox.Application.MarketData.Commands.CreateCandleImport;

/// <summary>Raw import parameters as the user sent them; <see cref="To"/> defaults to now. Returns the id of the created job.</summary>
public sealed record CreateCandleImportCommand(string Category, string Symbol, string Interval, string From, string? To)
    : IRequest<Guid>;
