using System.Text.RegularExpressions;
using TimingApp.Domain.SharedKernel;

namespace TimingApp.Domain.FinishRecording;

/// <summary>
/// Name of a race (FS2-07): letters, digits, spaces and <c>- _ .</c>, at most 120 characters, not starting or ending
/// with a space or a dot. It names the directory of the race's recordings, so names Windows reserves for devices
/// (<c>CON</c>, <c>NUL</c>, <c>COM1</c>, ...) are rejected as well.
/// </summary>
public sealed partial record RaceName
{
    public const int MaxLength = 120;

    private RaceName(string value) => Value = value;

    public string Value { get; }

    public static Result<RaceName> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Error.Validation("race.nameRequired");
        }
        if (value.Length > MaxLength || !Pattern().IsMatch(value) || ReservedName().IsMatch(value))
        {
            return Error.Validation("race.nameInvalid", new Dictionary<string, object?> { ["max"] = MaxLength });
        }
        return new RaceName(value);
    }

    public override string ToString() => Value;

    [GeneratedRegex(@"^[\p{L}\p{N}_\-](?:[\p{L}\p{N} _.\-]*[\p{L}\p{N}_\-])?$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex Pattern();

    [GeneratedRegex(@"^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])(\..*)?$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 100)]
    private static partial Regex ReservedName();
}

/// <summary>
/// A passage reported by the timing system (FS2-10): a start number crossed the finish line at <see cref="Time"/>
/// (on the shared time base, after the configured offset, FS2-18).
/// </summary>
public sealed partial record Passage
{
    public const int MaxStartNumberLength = 20;

    /// <summary>A passage is reported at most this long after it happened (FS2-17).</summary>
    public static readonly TimeSpan MaxReportDelay = TimeSpan.FromSeconds(5);

    private Passage(string startNumber, DateTimeOffset time)
    {
        StartNumber = startNumber;
        Time = time;
    }

    public string StartNumber { get; }

    public DateTimeOffset Time { get; }

    /// <summary>A start number of 1 to 20 letters, digits or dashes, e.g. <c>42</c> or <c>A-12</c>.</summary>
    public static Result<Passage> Create(string? startNumber, DateTimeOffset time)
    {
        var trimmed = startNumber?.Trim();
        return trimmed is { Length: > 0 and <= MaxStartNumberLength } && StartNumberPattern().IsMatch(trimmed)
            ? new Passage(trimmed, time)
            : Error.Validation("passage.startNumberInvalid", new Dictionary<string, object?> { ["max"] = MaxStartNumberLength });
    }

    [GeneratedRegex(@"^[A-Za-z0-9\-]+$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex StartNumberPattern();
}
