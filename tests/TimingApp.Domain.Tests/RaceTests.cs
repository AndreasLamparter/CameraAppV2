using TimingApp.Domain.FinishRecording;

namespace TimingApp.Domain.Tests;

public sealed class RaceTests
{
    [Theory]
    [InlineData("Lauf 1")]
    [InlineData("MTB-Cup_2026 Elite.Damen")]
    [InlineData("Ötztal Lauf ß")]
    [InlineData("-Start-")]
    public void RaceName_Allowed_IsCreated(string value) =>
        Assert.Equal(value, RaceName.Create(value).Value.Value);

    [Theory]
    [InlineData(null, "race.nameRequired")]
    [InlineData("  ", "race.nameRequired")]
    [InlineData("../Lauf", "race.nameInvalid")]
    [InlineData("Lauf/2", "race.nameInvalid")]
    [InlineData("Lauf.", "race.nameInvalid")]
    [InlineData(".Lauf", "race.nameInvalid")]
    [InlineData(" Lauf", "race.nameInvalid")]
    [InlineData("Lauf:1", "race.nameInvalid")]
    [InlineData("CON", "race.nameInvalid")]
    [InlineData("com1.txt", "race.nameInvalid")]
    public void RaceName_NotAllowed_ReturnsStableCode(string? value, string code) =>
        Assert.Equal(code, RaceName.Create(value).Error?.Code);

    [Fact]
    public void RaceName_LongerThan120_IsInvalid()
    {
        Assert.True(RaceName.Create(new string('a', 120)).IsSuccess);
        Assert.Equal("race.nameInvalid", RaceName.Create(new string('a', 121)).Error?.Code);
    }

    [Theory]
    [InlineData("42", "42")]
    [InlineData(" A-12 ", "A-12")]
    public void Passage_ValidStartNumber_IsTrimmed(string startNumber, string expected) =>
        Assert.Equal(expected, Passage.Create(startNumber, DateTimeOffset.UnixEpoch).Value.StartNumber);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("4 2")]
    [InlineData("42;")]
    [InlineData("123456789012345678901")]
    public void Passage_InvalidStartNumber_ReturnsStableCode(string? startNumber) =>
        Assert.Equal("passage.startNumberInvalid", Passage.Create(startNumber, DateTimeOffset.UnixEpoch).Error?.Code);

    [Fact]
    public void Settings_PassageOffsetOutOfRange_IsInvalid() =>
        Assert.Equal("settings.passageOffsetInvalid", (FinishRecordingSettings.Default with { PassageOffsetMs = 10_001 }).Validate().Error?.Code);
}
