using System.Diagnostics;
using System.Text.Json;
using ZZZBuffTracker.Application;
using ZZZBuffTracker.Domain;

namespace ZZZBuffTracker.Infrastructure;

public sealed class MonotonicClock : IClock
{
    private readonly long origin = Stopwatch.GetTimestamp();
    public TimeSpan Elapsed => Stopwatch.GetElapsedTime(origin);
}

public sealed class BuffStateStore : IBuffStateStore
{
    private OverlaySnapshot snapshot = OverlaySnapshot.Empty;
    public OverlaySnapshot Snapshot => Volatile.Read(ref snapshot);
    public void Publish(OverlaySnapshot value) => Volatile.Write(ref snapshot, value);
}

/// <summary>JSONL with session/event correlation; I/O failures do not terminate tracking.</summary>
public sealed class FileTrackingLog(string path) : ITrackingLog
{
    private readonly object gate = new();
    public void Write(string level, string message, Guid sessionId, Guid? eventId = null, Exception? exception = null)
    {
        try
        {
            lock (gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                // Bound disk use for long development sessions.
                if (File.Exists(path) && new FileInfo(path).Length > 2_000_000)
                    File.Move(path, path + ".previous", true);
                File.AppendAllText(path, JsonSerializer.Serialize(new
                { at = DateTimeOffset.UtcNow, level, message, sessionId, eventId, exception = exception?.ToString() }) + Environment.NewLine);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Trace.TraceError("Tracking log unavailable: {0}", ex.Message); }
    }
}
