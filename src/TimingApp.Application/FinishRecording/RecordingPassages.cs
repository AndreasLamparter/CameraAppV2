using TimingApp.Domain.FinishRecording;

namespace TimingApp.Application.FinishRecording;

/// <summary>
/// The passages assigned to one recording (FS2-11, FS2-14). Passages may still arrive after the recording has been
/// saved (up to 5 s late, FS2-17); from then on every new passage is reported to the saver, which rewrites the
/// metadata. Thread-safe: written by the processing loop, read by the saver.
/// </summary>
public sealed class RecordingPassages
{
    private readonly Lock _lock = new();
    private readonly List<Passage> _passages = [];
    private Action<RecordingId, IReadOnlyList<Passage>>? _savedUpdate;
    private RecordingId _saved;

    public RecordingPassages()
    {
    }

    public RecordingPassages(IEnumerable<Passage> passages) => _passages.AddRange(passages);

    public void Add(Passage passage)
    {
        Action<RecordingId, IReadOnlyList<Passage>>? update;
        IReadOnlyList<Passage> all;
        lock (_lock)
        {
            _passages.Add(passage);
            update = _savedUpdate;
            all = [.. _passages];
        }
        update?.Invoke(_saved, all);
    }

    public IReadOnlyList<Passage> Snapshot()
    {
        lock (_lock)
        {
            return [.. _passages];
        }
    }

    /// <summary>
    /// Called by the saver after writing the metadata with <paramref name="written"/> passages: later passages go
    /// to <paramref name="update"/>. Returns all passages when some arrived in between, otherwise null.
    /// </summary>
    internal IReadOnlyList<Passage>? MarkSaved(RecordingId id, int written, Action<RecordingId, IReadOnlyList<Passage>> update)
    {
        lock (_lock)
        {
            _saved = id;
            _savedUpdate = update;
            return _passages.Count > written ? [.. _passages] : null;
        }
    }
}
