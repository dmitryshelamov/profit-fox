using MediatR;

namespace ProfitFox.Application.MarketData.Queries.SearchInstruments;

/// <summary>Raw search parameters as the user sent them; <see cref="Limit" /> defaults to <see cref="SearchInstrumentsQueryHandler.DefaultLimit" />.</summary>
public sealed record SearchInstrumentsQuery(string Category, string Query, int? Limit) : IRequest<SearchInstrumentsResult>;