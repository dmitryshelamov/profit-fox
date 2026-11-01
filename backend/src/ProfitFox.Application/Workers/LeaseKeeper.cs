namespace ProfitFox.Application.Workers;

/// <summary>Keeps the lease of running work: renews it every <see cref="WorkerSettings.LeaseRenewal" /> and cancels the work once it is lost.</summary>
public static class LeaseKeeper
{
    /// <summary>
    ///     A renewal that fails, such as on a lost database connection, is tried again on the next interval while the lease still holds;
    ///     the work stops when the next try would come after the lease expired, since another Worker may take it over then.
    /// </summary>
    /// <param name="renew">Extends the lease until the given time; <c>false</c> when this Worker no longer holds it.</param>
    /// <param name="settings">How long a lease lasts and how often it is renewed.</param>
    /// <param name="time">The clock of the lease.</param>
    /// <param name="work">Cancelled when the lease is lost.</param>
    /// <param name="done">Cancelled by the caller when the work ends; the keeper then stops.</param>
    public static async Task KeepAsync(
        Func<DateTimeOffset, CancellationToken, Task<bool>> renew,
        WorkerSettings settings,
        TimeProvider time,
        CancellationTokenSource work,
        CancellationToken done)
    {
        // The work starts right after its lease is taken.
        DateTimeOffset leasedUntil = time.GetUtcNow() + settings.LeaseDuration;

        try
        {
            while (true)
            {
                await Task.Delay(settings.LeaseRenewal, time, done);
                DateTimeOffset until = time.GetUtcNow() + settings.LeaseDuration;
                bool? renewed;

                try
                {
                    renewed = await renew(until, done);
                }
                catch (Exception) when (!done.IsCancellationRequested)
                {
                    renewed = null;
                }

                if (renewed == true)
                {
                    leasedUntil = until;
                }
                else if (renewed == false || time.GetUtcNow() + settings.LeaseRenewal >= leasedUntil)
                {
                    await work.CancelAsync();

                    return;
                }
            }
        }
        catch (OperationCanceledException) when (done.IsCancellationRequested)
        {
            // The work ended.
        }
    }
}