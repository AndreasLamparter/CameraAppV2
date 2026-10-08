using TimingApp.Domain.FinishRecording;

namespace TimingApp.Domain.Tests;

public sealed class SettingsAndValuesTests
{
    private static readonly FinishRecordingSettings Default = FinishRecordingSettings.Default;

    [Fact]
    public void Default_MatchesFeatureSetStandardValues()
    {
        var d = Default.Detection;

        Assert.Equal(25, d.PixelThreshold);
        Assert.Equal(2, d.OccupancyThresholdPercent);
        Assert.Equal(TimeSpan.FromSeconds(0.5), d.PreRoll);
        Assert.Equal(TimeSpan.FromSeconds(1.0), d.PostRoll);
        Assert.Equal(TimeSpan.FromSeconds(60), d.MaxDuration);
        Assert.Equal(TimeSpan.FromSeconds(2.0), d.FrontPreRoll);
        Assert.Equal(TimeSpan.FromSeconds(2.0), d.FrontPostRoll);
        Assert.True(Default.Validate().IsSuccess);
    }

    public static TheoryData<FinishRecordingSettings, string> InvalidSettings => new()
    {
        { Default with { MediaDirectory = "relative/path" }, "settings.mediaDirectoryInvalid" },
        { Default with { FinishCamera = Default.FinishCamera with { FrameRate = 0 } }, "settings.frameRateInvalid" },
        { Default with { FrontCamera = Default.FrontCamera with { Width = 100 } }, "settings.resolutionInvalid" },
        { Default with { FinishCamera = Default.FinishCamera with { OffsetMs = 1001 } }, "settings.offsetInvalid" },
        { Default with { FinishCamera = Default.FinishCamera with { Exposure = double.NaN } }, "settings.exposureInvalid" },
        { Default with { FrontCamera = Default.FrontCamera with { DeviceIndex = 0 } }, "settings.sameDevice" },
        { WithDevices(new("SVPRO", @"\\?\usb#1"), new("SVPRO", @"\\?\USB#1")), "settings.sameDevice" },
        { WithDevices(new("SVPRO", @"\\?\usb#1"), new("SVPRO", null)), "settings.sameDevice" },
        { WithDevices(new(" ", null), new("Webcam", null)), "settings.deviceNameInvalid" },
        { WithDevices(new(new string('x', 257), null), new("Webcam", null)), "settings.deviceNameInvalid" },
        { Default with { FinishLine = Default.FinishLine with { Position = 1920 } }, "settings.linePositionInvalid" },
        { Default with { FinishLine = Default.FinishLine with { Width = 0 } }, "settings.lineWidthInvalid" },
        { Default with { Detection = Default.Detection with { PixelThreshold = 0 } }, "settings.pixelThresholdInvalid" },
        { Default with { Detection = Default.Detection with { OccupancyThresholdPercent = 0 } }, "settings.occupancyThresholdInvalid" },
        { Default with { Detection = Default.Detection with { PostRoll = TimeSpan.Zero } }, "settings.durationInvalid" },
    };

    [Theory]
    [MemberData(nameof(InvalidSettings))]
    public void Validate_InvalidValue_ReturnsStableCode(FinishRecordingSettings settings, string code)
    {
        var result = settings.Validate();

        Assert.Equal(code, result.Error?.Code);
    }

    private static FinishRecordingSettings WithDevices(CameraDeviceRef finish, CameraDeviceRef front) => Default with
    {
        FinishCamera = Default.FinishCamera with { Device = finish },
        FrontCamera = Default.FrontCamera with { Device = front },
    };

    [Fact]
    public void Validate_TwoEqualCameraModelsWithDifferentPaths_AreDifferentDevices()
    {
        var settings = WithDevices(new("SVPRO", @"\\?\usb#1"), new("SVPRO", @"\\?\usb#2"));

        Assert.True(settings.Validate().IsSuccess);
    }

    [Fact]
    public void Validate_FrontCameraDisabled_AllowsSameDeviceIndex()
    {
        var settings = Default with { FrontCameraEnabled = false, FrontCamera = Default.FrontCamera with { DeviceIndex = 0 } };

        Assert.True(settings.Validate().IsSuccess);
    }

    [Theory]
    [InlineData(ImageRotation.Clockwise90, 1078, true)]
    [InlineData(ImageRotation.Clockwise90, 1079, false)]
    [InlineData(ImageRotation.CounterClockwise90, 1079, false)]
    [InlineData(ImageRotation.Rotate180, 1918, true)]
    [InlineData(ImageRotation.Rotate180, 1919, false)]
    public void Validate_RotatedImage_LineIsCheckedAgainstTheTurnedImageWidth(ImageRotation rotation, int position, bool valid)
    {
        var settings = Default with { FinishLine = new FinishLineSettings(rotation, position, 2, false) };

        Assert.Equal(valid, settings.Validate().IsSuccess);
        Assert.Equal(valid ? null : "settings.linePositionInvalid", settings.Validate().Error?.Code);
    }

    [Fact]
    public void Validate_UndefinedRotation_ReturnsStableCode()
    {
        var settings = Default with { FinishLine = Default.FinishLine with { Rotation = (ImageRotation)7 } };

        Assert.Equal("settings.rotationInvalid", settings.Validate().Error?.Code);
    }

    [Theory]
    [InlineData(80, 90, true)]
    [InlineData(85.4, 90, true)]
    [InlineData(85.5, 90, false)]
    [InlineData(90, 90, false)]
    public void LineRate_BelowNinetyFivePercent_IsTooLow(double measured, int configured, bool expected) =>
        Assert.Equal(expected, LineRate.IsTooLow(measured, configured));

    [Fact]
    public void LineRateMeter_MeasuresColumnsPerSecond_AfterOneSecond()
    {
        var meter = new LineRateMeter();
        var t0 = DateTimeOffset.UnixEpoch;
        for (var i = 0; i < 80; i++)
        {
            meter.Add(t0.AddSeconds(i / 80.0));
        }
        Assert.Null(meter.Rate);

        for (var i = 80; i < 200; i++)
        {
            meter.Add(t0.AddSeconds(i / 80.0));
        }

        Assert.Equal(80, meter.Rate!.Value, 1);
    }

    [Fact]
    public void RecordingId_FromStartTime_UsesUtcAndIsParseable()
    {
        var start = new DateTimeOffset(2026, 10, 7, 12, 0, 1, 234, TimeSpan.FromHours(2));

        var id = RecordingId.For(start, suffix: 2);

        Assert.Equal("20261007-100001-234-2", id.Value);
        Assert.True(RecordingId.Parse(id.Value).IsSuccess);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("..")]
    [InlineData("..\\20261007-100001-234")]
    [InlineData("20261007-100001-234/../x")]
    [InlineData("20261007-100001")]
    public void RecordingId_InvalidOrTraversal_IsRejected(string? value) =>
        Assert.Equal("recording.notFound", RecordingId.Parse(value).Error?.Code);
}
