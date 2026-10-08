namespace TimingApp.Application.FinishRecording;

/// <summary>Selection of time-ordered items that covers a time range completely.</summary>
public static class TimeRange
{
    /// <summary>
    /// Items within [from, to] plus the last item at or before <paramref name="from"/> and the first item at or after
    /// <paramref name="to"/>, so the selection starts no later than <paramref name="from"/> and ends no earlier than
    /// <paramref name="to"/> whenever such items exist. <paramref name="items"/> must be ordered by time.
    /// </summary>
    public static List<T> Covering<T>(IReadOnlyList<T> items, Func<T, DateTimeOffset> timestamp, DateTimeOffset from, DateTimeOffset to)
    {
        var first = 0;
        while (first + 1 < items.Count && timestamp(items[first + 1]) <= from)
        {
            first++;
        }
        var last = items.Count - 1;
        while (last - 1 >= 0 && timestamp(items[last - 1]) >= to)
        {
            last--;
        }
        var result = new List<T>();
        for (var i = first; i <= last; i++)
        {
            result.Add(items[i]);
        }
        return result;
    }
}
