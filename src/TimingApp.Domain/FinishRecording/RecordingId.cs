using System.Globalization;
using System.Text.RegularExpressions;
using TimingApp.Domain.SharedKernel;

namespace TimingApp.Domain.FinishRecording;

/// <summary>
/// Identifier of a recording, derived from the UTC time of its first column (<c>yyyyMMdd-HHmmss-fff</c>, with an
/// optional <c>-n</c> suffix on collision). Also the name of the recording directory, so it is validated strictly
/// against a fixed pattern (no path traversal).
/// </summary>
public readonly partial record struct RecordingId
{
    private RecordingId(string value) => Value = value;

    public string Value { get; }

    public static RecordingId For(DateTimeOffset start, int suffix = 0)
    {
        var value = start.UtcDateTime.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        return new RecordingId(suffix > 0 ? $"{value}-{suffix.ToString(CultureInfo.InvariantCulture)}" : value);
    }

    public static Result<RecordingId> Parse(string? value) =>
        value is not null && Pattern().IsMatch(value)
            ? new RecordingId(value)
            : Error.NotFound("recording.notFound");

    public override string ToString() => Value;

    [GeneratedRegex(@"^\d{8}-\d{6}-\d{3}(-\d{1,3})?$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex Pattern();
}
