namespace SOUNDBOY.Model;

public sealed class Playlist
{
    readonly List<Track> items = new();
    Track? current;
    List<Track>? shuffleOrder;

    public event Action? Changed;

    public IReadOnlyList<Track> Items => items;
    public int Count => items.Count;

    public Track? Current
    {
        get => current;
        set { current = value; Changed?.Invoke(); }
    }

    public int CurrentIndex => current == null ? -1 : items.IndexOf(current);
    public IEnumerable<Track> Selected => items.Where(t => t.Selected);
    public TimeSpan TotalDuration => TimeSpan.FromTicks(items.Sum(t => t.Duration?.Ticks ?? 0));

    public void NotifyChanged() => Changed?.Invoke();

    void Structural()
    {
        shuffleOrder = null;
        Changed?.Invoke();
    }

    public void ResetShuffle() => shuffleOrder = null;

    public void Add(IEnumerable<Track> tracks, int at = -1)
    {
        var list = tracks.ToList();
        if (at < 0 || at > items.Count) items.AddRange(list);
        else items.InsertRange(at, list);
        Structural();
    }

    public void Clear() { items.Clear(); Structural(); }
    public void RemoveSelected() { items.RemoveAll(t => t.Selected); Structural(); }
    public void Crop() { items.RemoveAll(t => !t.Selected); Structural(); }
    public void RemoveMissing() { items.RemoveAll(t => !t.IsUrl && !File.Exists(t.Path)); Structural(); }

    public void RemoveDuplicates()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        items.RemoveAll(t => !seen.Add(t.Path));
        Structural();
    }

    public void SelectAll(bool on)
    {
        foreach (var t in items) t.Selected = on;
        Changed?.Invoke();
    }

    public void InvertSelection()
    {
        foreach (var t in items) t.Selected = !t.Selected;
        Changed?.Invoke();
    }

    public void SelectOnly(int index)
    {
        for (int i = 0; i < items.Count; i++) items[i].Selected = i == index;
        Changed?.Invoke();
    }

    public void SelectRange(int a, int b)
    {
        int lo = Math.Min(a, b), hi = Math.Max(a, b);
        for (int i = 0; i < items.Count; i++) items[i].Selected = i >= lo && i <= hi;
        Changed?.Invoke();
    }

    public void SortBy<TKey>(Func<Track, TKey> key, IComparer<TKey>? comparer = null)
    {
        var sorted = items.OrderBy(key, comparer).ToList();
        items.Clear();
        items.AddRange(sorted);
        Structural();
    }

    public void Reverse() { items.Reverse(); Structural(); }

    public void Randomize()
    {
        for (int i = items.Count - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
        Structural();
    }

    /// <summary>Moves the selected block by up to <paramref name="delta"/> rows. Returns the rows actually moved.</summary>
    public int MoveSelected(int delta)
    {
        var idx = Enumerable.Range(0, items.Count).Where(i => items[i].Selected).ToList();
        if (idx.Count == 0 || delta == 0) return 0;
        delta = Math.Clamp(delta, -idx[0], items.Count - 1 - idx[^1]);
        for (int step = 0; step < Math.Abs(delta); step++)
        {
            if (delta < 0)
            {
                for (int i = 1; i < items.Count; i++)
                    if (items[i].Selected && !items[i - 1].Selected)
                        (items[i], items[i - 1]) = (items[i - 1], items[i]);
            }
            else
            {
                for (int i = items.Count - 2; i >= 0; i--)
                    if (items[i].Selected && !items[i + 1].Selected)
                        (items[i], items[i + 1]) = (items[i + 1], items[i]);
            }
        }
        if (delta != 0) Changed?.Invoke();
        return delta;
    }

    List<Track> ShuffleOrder()
    {
        if (shuffleOrder == null || shuffleOrder.Count != items.Count)
        {
            shuffleOrder = items.ToList();
            for (int i = shuffleOrder.Count - 1; i > 0; i--)
            {
                int j = Random.Shared.Next(i + 1);
                (shuffleOrder[i], shuffleOrder[j]) = (shuffleOrder[j], shuffleOrder[i]);
            }
            if (current != null && shuffleOrder.Remove(current)) shuffleOrder.Insert(0, current);
        }
        return shuffleOrder;
    }

    public Track? GetNext(bool shuffle, bool wrap)
    {
        if (items.Count == 0) return null;
        if (!shuffle)
        {
            int i = CurrentIndex;
            if (i + 1 < items.Count) return items[i + 1];
            return wrap ? items[0] : null;
        }
        var order = ShuffleOrder();
        int k = current == null ? -1 : order.IndexOf(current);
        if (k + 1 < order.Count) return order[k + 1];
        if (!wrap) return null;
        shuffleOrder = null;
        order = ShuffleOrder();
        return order.Count > 1 ? order[1] : order[0];
    }

    public Track? GetPrevious(bool shuffle, bool wrap)
    {
        if (items.Count == 0) return null;
        if (!shuffle)
        {
            int i = CurrentIndex;
            if (i < 0) return items[0];
            if (i > 0) return items[i - 1];
            return wrap ? items[^1] : items[0];
        }
        var order = ShuffleOrder();
        int k = current == null ? 0 : order.IndexOf(current);
        if (k > 0) return order[k - 1];
        return wrap ? order[^1] : order[0];
    }
}
