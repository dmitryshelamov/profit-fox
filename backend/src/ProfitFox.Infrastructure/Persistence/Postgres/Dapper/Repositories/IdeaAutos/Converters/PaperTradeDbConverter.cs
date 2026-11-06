using ProfitFox.Domain.Entities.IdeaAutos;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.IdeaTrades.Contracts;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.IdeaTrades.Converters;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.IdeaAutos.Converters;

internal static class PaperTradeDbConverter
{
    internal static IdeaTradeDb FromDomain(PaperIdeaTrade paper)
    {
        return IdeaTradeDbConverter.FromDomain(paper.Trade, paper.SteppedUntil);
    }

    internal static PaperIdeaTrade ToDomain(IdeaTradeDb row)
    {
        return new PaperIdeaTrade(IdeaTradeDbConverter.ToDomain(row), row.AutoId!.Value, row.SteppedUntil!.Value);
    }
}