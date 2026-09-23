using System.Collections.Concurrent;

namespace SOUNDBOY.Model;

/// <summary>Reads tags for newly added tracks on a background thread.</summary>
public sealed class TagLoader : IDisposable
{
    readonly BlockingCollection<Track> queue = new();
    int dirty;

    public TagLoader()
    {
        new Thread(Run) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "Tag loader" }.Start();
    }

    public void Enqueue(IEnumerable<Track> tracks)
    {
        if (queue.IsAddingCompleted) return;
        foreach (var t in tracks) queue.Add(t);
    }

    void Run()
    {
        foreach (var t in queue.GetConsumingEnumerable())
        {
            if (t.InfoLoaded) continue;
            t.LoadInfo();
            Interlocked.Exchange(ref dirty, 1);
        }
    }

    /// <summary>True once after any tags have been loaded since the last call.</summary>
    public bool TakeDirty() => Interlocked.Exchange(ref dirty, 0) == 1;

    public void Dispose() => queue.CompleteAdding();
}
