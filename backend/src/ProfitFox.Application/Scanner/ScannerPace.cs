namespace ProfitFox.Application.Scanner;

/// <summary>Whether the minute cycle keeps time: the replay of history pauses while the last snapshot came late (decision 1).</summary>
public sealed class ScannerPace
{
    /// <summary>A snapshot later than this after it was due is late.</summary>
    public static readonly TimeSpan Tolerance = TimeSpan.FromSeconds(2);

    private long _lateTicks;

    public bool IsLate => Interlocked.Read(ref _lateTicks) > Tolerance.Ticks;

    /// <summary>The snapshot of a minute was due at <paramref name="due" /> and written at <paramref name="written" />.</summary>
    public void Snapshot(DateTimeOffset due, DateTimeOffset written)
    {
        Interlocked.Exchange(ref _lateTicks, (written - due).Ticks);
    }
}