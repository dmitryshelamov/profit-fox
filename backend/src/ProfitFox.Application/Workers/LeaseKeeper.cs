namespace ProfitFox.Application.Workers;

/// <summary>Keeps the lease of running work: renews it every <see cref="WorkerSettings.LeaseRenewal" /> and cancels the work once it is lost.</summary>
public static class LeaseKeeper
{
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
        try
        {
            while (true)
            {
                await Task.Delay(settings.LeaseRenewal, time, done);

                if (!await renew(time.GetUtcNow() + settings.LeaseDuration, done))
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