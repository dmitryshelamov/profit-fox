using MediatR;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.MarketData.Commands.CreateCandleImport;

public sealed class CreateCandleImportCommandHandler(
    IMarketHistoryClient marketHistory,
    ICandleImportJobRepository jobs,
    ICandleImportQueue queue,
    TimeProvider time) : IRequestHandler<CreateCandleImportCommand, Guid>
{
    /// <summary>Validates the request, checks the symbol on the exchange and queues a pending job.</summary>
    /// <exception cref="ValidationFailedException">A parameter is invalid or the exchange has no such symbol.</exception>
    /// <exception cref="ExchangeException">The exchange could not confirm the symbol.</exception>
    public async Task<Guid> Handle(CreateCandleImportCommand request, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string>();
        if (!MarketDataCodes.TryParseCategory(request.Category, out MarketCategory category))
        {
            errors["category"] = "Category must be spot or linear.";
        }

        if (!MarketDataCodes.TryParseInterval(request.Interval, out Interval interval))
        {
            errors["interval"] = "Interval must be one of Bybit intervals: 1, 3, 5, 15, 30, 60, 120, 240, 360, 720, D, W, M.";
        }

        DateTimeOffset now = time.GetUtcNow();
        if (!ApiTimestamps.TryParse(request.From, out DateTimeOffset from))
        {
            errors["from"] = ApiTimestamps.Invalid("from");
        }

        DateTimeOffset to = now;
        bool toIsTime = request.To is null || ApiTimestamps.TryParse(request.To, out to);
        if (!toIsTime)
        {
            errors["to"] = ApiTimestamps.Invalid("to");
        }

        // An unparsed `from` stays at its minimum and never fails the period checks.
        from = from.ToUniversalTime();
        to = to.ToUniversalTime();
        if (toIsTime && from >= to)
        {
            errors["from"] = "from must be earlier than to.";
        }
        else if (from > now)
        {
            errors["from"] = "from must not be in the future.";
        }

        if (errors.Count > 0)
        {
            throw new ValidationFailedException(errors);
        }

        var instrument = new Instrument(category, request.Symbol);
        if (!await marketHistory.InstrumentExistsAsync(instrument, cancellationToken))
        {
            throw new ValidationFailedException(new Dictionary<string, string> { ["symbol"] = $"Bybit has no {instrument.Symbol} in {request.Category}." });
        }

        var job = new CandleImportJob(Guid.NewGuid(), instrument, interval, from, to, CandleImportStatus.Pending, ImportedCandles: 0, Error: null, now, StartedAt: null, FinishedAt: null);
        await jobs.AddAsync(job, cancellationToken);
        await queue.EnqueueAsync(job.Id, cancellationToken);
        return job.Id;
    }
}
