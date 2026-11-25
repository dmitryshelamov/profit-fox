using System.Globalization;
using System.Threading.Channels;

using MediatR;

using Microsoft.Extensions.Logging;

using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Application.Ports.MarketData.Feed;
using ProfitFox.Application.Services.Ideas;
using ProfitFox.Application.Services.Market;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.Commands.Ideas.RunScanner;

/// <summary>
///     The scanner on the live feeds: the events of the feed go to a queue of their own as they come, and the scanner takes
///     them in their order and looks at its clock at least every <see cref="ScannerSettings.TickInterval" />. It tells the
///     lease what it does every few seconds. An error of a pass is logged and the next pass goes on.
/// </summary>
internal sealed class RunScannerCommandHandler(
    IMarketFeed feed,
    IRecentHistory history,
    IIdeaRepository ideas,
    IMarketPairRepository market,
    IScannerLeaseRepository lease,
    IdeaLibrary library,
    TimeProvider time,
    ScannerSettings settings,
    IdeasSettings ideasSettings,
    ILogger<RunScannerCommandHandler> logger) : IRequestHandler<RunScannerCommand, bool>
{
    private static readonly TimeSpan ReportInterval = TimeSpan.FromSeconds(5);

    public async Task<bool> Handle(RunScannerCommand request, CancellationToken cancellationToken)
    {
        var scanner = new IdeaScanner(new MarketBook(time, settings.CloseDelay), history, ideas, market, library, time, settings, ideasSettings, logger);
        await scanner.StartAsync(cancellationToken);
        logger.LogInformation("The scanner of ideas starts on {Worker} from {Since}.", request.Worker, scanner.CatchUpFrom?.ToString("O", CultureInfo.InvariantCulture) ?? "the whole day the feed holds");
        // A bounded queue (task 18.12): while the scanner is slow the feed is not read on, so the catch-up of a day does not
        // pile up in memory; nothing is dropped.
        var queue = Channel.CreateBounded<MarketEvent>(new BoundedChannelOptions(Math.Max(1, settings.QueueCapacity))
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait
        });
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task pump = Pump(scanner.CatchUpFrom, queue.Writer, stop.Token);
        DateTimeOffset reported = DateTimeOffset.MinValue;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                Task<bool> waiting = queue.Reader.WaitToReadAsync(cancellationToken).AsTask();
                await Task.WhenAny(waiting, Task.Delay(settings.TickInterval, time, cancellationToken));
                try
                {
                    // Nothing came for a tick: the feed is not catching up (task 19.9).
                    if (!waiting.IsCompleted)
                    {
                        scanner.Quiet();
                    }

                    while (queue.Reader.TryRead(out MarketEvent? item))
                    {
                        await scanner.ApplyAsync(item, cancellationToken);
                    }

                    await scanner.TickAsync(cancellationToken);
                    if (time.GetUtcNow() - reported >= ReportInterval)
                    {
                        (int warmed, int pairs, TimeSpan? left) = scanner.WarmupProgress();
                        (int failed, string? failure) = scanner.Failures;
                        await lease.ReportAsync(request.Worker, scanner.LiveSince, warmed, pairs, cancellationToken, failed, failure, left);
                        reported = time.GetUtcNow();
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogError(exception, "A pass of the scanner of ideas failed.");
                }

                if (pump.IsFaulted)
                {
                    await pump;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The Worker stops or lost the lease.
        }
        finally
        {
            await stop.CancelAsync();
            try
            {
                await pump;
            }
            catch (OperationCanceledException)
            {
                // The feed stops with the scanner.
            }
        }

        return true;
    }

    private async Task Pump(DateTimeOffset? since, ChannelWriter<MarketEvent> queue, CancellationToken cancellationToken)
    {
        await foreach (MarketEvent item in feed.Subscribe(new MarketSubscription(since, WholeDay: true), cancellationToken))
        {
            await queue.WriteAsync(item, cancellationToken);
        }
    }
}
