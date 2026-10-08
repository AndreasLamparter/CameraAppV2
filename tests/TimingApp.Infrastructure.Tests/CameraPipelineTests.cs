using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCvSharp;
using TimingApp.Application.FinishRecording;
using TimingApp.Domain.FinishRecording;
using TimingApp.Infrastructure.Camera;
using TimingApp.Infrastructure.Camera.Capture;
using TimingApp.Infrastructure.Camera.Preview;
using TimingApp.Infrastructure.Camera.Rendering;
using TimingApp.Infrastructure.Camera.Sources;
using TimingApp.Infrastructure.Camera.Video;

namespace TimingApp.Infrastructure.Tests;

public sealed class CameraPipelineTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 7, 10, 0, 0, TimeSpan.Zero);
    private static readonly FinishRecordingSettings Defaults = FinishRecordingSettings.Default;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static CameraSessionOptions Options(int finishOffsetMs = 0, int frontOffsetMs = 0, bool front = true) => new(
        Defaults.FinishCamera with { Width = 320, Height = 240, OffsetMs = finishOffsetMs },
        new FinishLineSettings(ImageRotation.None, 100, 3, false),
        front ? Defaults.FrontCamera with { Width = 320, Height = 240, OffsetMs = frontOffsetMs } : null,
        TimeSpan.FromSeconds(30));

    private static CameraSystem System(ICameraSourceFactory sources, TimeProvider time, CameraOptions? options = null, ICameraDeviceEnumerator? devices = null) =>
        new(sources, devices ?? new FakeDeviceEnumerator(), new CaptureClock(time), options ?? new CameraOptions(), NullLogger<CameraSystem>.Instance);

    private static async Task<List<LineColumn>> ReadColumnsAsync(ICameraSession session, int count)
    {
        var columns = new List<LineColumn>();
        while (columns.Count < count)
        {
            columns.Add(await session.Columns.ReadAsync(Ct).AsTask().WaitAsync(TimeSpan.FromSeconds(10), Ct));
        }
        return columns;
    }

    [Fact]
    public async Task Session_DeliversFinishColumnsAndBuffersFrontFrames()
    {
        using var system = System(new FakeSourceFactory(_ => new FakeSource(320, 240, 77)), TimeProvider.System);

        var session = await system.StartAsync(Options(), Ct);
        var columns = await ReadColumnsAsync(session, 5);
        await session.FrontFrames!.WaitForAsync(DateTimeOffset.MinValue, TimeSpan.FromSeconds(5), Ct);
        await Task.Delay(200, Ct);
        await session.DisposeAsync();

        Assert.All(columns, c => Assert.Equal(240 * 3, c.Bgr.Length));
        Assert.All(columns, c => Assert.All(c.Bgr, b => Assert.Equal(77, b)));
        Assert.NotEmpty(session.FrontFrames.Snapshot(DateTimeOffset.MinValue, DateTimeOffset.MaxValue));
        // Stopping completes the column stream: the remaining columns can be drained.
        await session.Columns.ReadAllAsync(Ct).CountAsync(Ct).AsTask().WaitAsync(TimeSpan.FromSeconds(5), Ct);
        Assert.Equal(CameraState.Off, session.Health(CameraRole.Finish).State);
    }

    [Fact]
    public async Task Szenario_KameraOffsetWirdAngewendet()
    {
        var time = new ManualTimeProvider(T0);
        using var system = System(new FakeSourceFactory(_ => new FakeSource(320, 240, 50)), time);

        var session = await system.StartAsync(Options(finishOffsetMs: 0, frontOffsetMs: -40), Ct);
        var column = (await ReadColumnsAsync(session, 1))[0];
        await session.FrontFrames!.WaitForAsync(DateTimeOffset.MinValue, TimeSpan.FromSeconds(5), Ct);
        var frame = session.FrontFrames.Snapshot(DateTimeOffset.MinValue, DateTimeOffset.MaxValue)[0];
        await session.DisposeAsync();

        // The clock does not move: without offset, both cameras would report exactly T0.
        Assert.Equal(T0, column.Timestamp);
        Assert.Equal(T0.AddMilliseconds(-40), frame.Timestamp);
    }

    [Fact]
    public async Task Szenario_FrontkameraFehlt_FinishCameraKeepsRunning()
    {
        var sources = new FakeSourceFactory(role => new FakeSource(320, 240, 60, failOpen: role == CameraRole.Front));
        using var system = System(sources, TimeProvider.System);

        var session = await system.StartAsync(Options(), Ct);
        await ReadColumnsAsync(session, 3);
        await Task.Delay(100, Ct);

        Assert.Equal(new CameraHealth(CameraState.Error, "camera.openFailed", 0), session.Health(CameraRole.Front));
        Assert.Equal(CameraState.Running, session.Health(CameraRole.Finish).State);
        await session.DisposeAsync();
    }

    [Fact]
    public async Task Health_CameraDeliversNoFrames_ReportsNoFrames()
    {
        var options = new CameraOptions { NoFramesTimeout = TimeSpan.FromMilliseconds(200) };
        using var system = System(new FakeSourceFactory(_ => new FakeSource(320, 240, 60, noFrames: true)), TimeProvider.System, options);

        var session = await system.StartAsync(Options(front: false), Ct);
        await Task.Delay(500, Ct);

        Assert.Equal("camera.noFrames", session.Health(CameraRole.Finish).ErrorCode);
        Assert.Equal(CameraState.Disabled, session.Health(CameraRole.Front).State);
        await session.DisposeAsync();
    }

    [Fact]
    public async Task StartAsync_AfterStop_WaitsForReleaseAndReleasesSources()
    {
        var created = new List<FakeSource>();
        var sources = new FakeSourceFactory(_ =>
        {
            var source = new FakeSource(320, 240, 60);
            lock (created)
            {
                created.Add(source);
            }
            return source;
        });
        using var system = System(sources, TimeProvider.System);

        var first = await system.StartAsync(Options(), Ct);
        await first.DisposeAsync();
        var second = await system.StartAsync(Options(), Ct);
        await second.DisposeAsync();

        Assert.Equal(4, created.Count);
        Assert.All(created, s => Assert.True(s.Disposed));
    }

    [Fact]
    public async Task SyntheticSource_SimulatedRider_ChangesTheFinishLine()
    {
        var control = new SimulationControl();
        var options = new CameraOptions { Simulation = new SimulationOptions { Enabled = true } };
        using var system = System(new CameraSourceFactory(options, control), TimeProvider.System, options);
        var sessionOptions = Options(front: false) with { FinishCamera = Defaults.FinishCamera with { Width = 320, Height = 240, FrameRate = 100 } };

        var session = await system.StartAsync(sessionOptions, Ct);
        var free = (await ReadColumnsAsync(session, 2))[^1];
        control.Occupied = true;
        await ReadColumnsAsync(session, 3);
        var occupied = (await ReadColumnsAsync(session, 1))[0];
        await session.DisposeAsync();

        var changed = Enumerable.Range(0, 240).Count(i => Math.Abs(free.Bgr[i * 3 + 2] - occupied.Bgr[i * 3 + 2]) > 25);
        Assert.InRange(changed, 100, 140);
    }

    [Fact]
    public async Task FrontFrameBuffer_DropsFramesOlderThanRetention_AndReleasesWaitersOnComplete()
    {
        var buffer = new FrontFrameBuffer(TimeSpan.FromSeconds(2));
        for (var i = 0; i < 40; i++)
        {
            buffer.Add(new EncodedFrame(T0.AddSeconds(i * 0.1), [1]));
        }

        Assert.Equal(T0.AddSeconds(1.9), buffer.Snapshot(DateTimeOffset.MinValue, DateTimeOffset.MaxValue)[0].Timestamp);
        var waiting = buffer.WaitForAsync(T0.AddSeconds(10), TimeSpan.FromSeconds(10), Ct);
        Assert.False(waiting.IsCompleted);
        buffer.Complete();
        await waiting.WaitAsync(TimeSpan.FromSeconds(1), Ct);
    }

    [Fact]
    public async Task FrontFrameBuffer_WaitFor_CompletesWhenFrameArrives()
    {
        var buffer = new FrontFrameBuffer(TimeSpan.FromSeconds(2));

        var waiting = buffer.WaitForAsync(T0.AddSeconds(1), TimeSpan.FromSeconds(10), Ct);
        buffer.Add(new EncodedFrame(T0, [1]));
        Assert.False(waiting.IsCompleted);
        buffer.Add(new EncodedFrame(T0.AddSeconds(1.01), [1]));

        await waiting.WaitAsync(TimeSpan.FromSeconds(1), Ct);
    }

    [Fact]
    public void LineExtractor_VerticalLine_AveragesOverTheLineWidth()
    {
        using var frame = new Mat(4, 6, MatType.CV_8UC3, Scalar.Black);
        frame.Set(1, 2, new Vec3b(10, 20, 30));
        frame.Set(1, 3, new Vec3b(30, 40, 50));

        var column = LineExtractor.Extract(frame, new FinishLineSettings(ImageRotation.None, 2, 2, false));

        Assert.Equal(4 * 3, column.Length);
        Assert.Equal(new byte[] { 20, 30, 40 }, column[3..6]);
        Assert.Equal(new byte[] { 0, 0, 0 }, column[..3]);
    }

    [Theory]
    [InlineData(ImageRotation.None)]
    [InlineData(ImageRotation.Clockwise90)]
    [InlineData(ImageRotation.CounterClockwise90)]
    [InlineData(ImageRotation.Rotate180)]
    public void LineExtractor_RotatedImage_ReadsTheColumnOfTheTurnedImageFromTopToBottom(ImageRotation rotation)
    {
        // A camera image with distinct pixels; the expected column is taken from the image actually turned by OpenCV.
        using var frame = new Mat(4, 6, MatType.CV_8UC3);
        for (var y = 0; y < frame.Rows; y++)
        {
            for (var x = 0; x < frame.Cols; x++)
            {
                frame.Set(y, x, new Vec3b((byte)(x * 10), (byte)(y * 10), (byte)((x * 10) + y)));
            }
        }
        var line = new FinishLineSettings(rotation, 1, 2, false);
        var turned = LineExtractor.Upright(frame, rotation);

        var column = LineExtractor.Extract(frame, line);

        Assert.Equal(turned.Rows * 3, column.Length);
        for (var i = 0; i < turned.Rows; i++)
        {
            var a = turned.At<Vec3b>(i, 1);
            var b = turned.At<Vec3b>(i, 2);
            Assert.Equal((byte)((a.Item0 + b.Item0 + 1) / 2), column[i * 3]);
            Assert.Equal((byte)((a.Item1 + b.Item1 + 1) / 2), column[(i * 3) + 1]);
            Assert.Equal((byte)((a.Item2 + b.Item2 + 1) / 2), column[(i * 3) + 2]);
        }
        if (!ReferenceEquals(turned, frame))
        {
            turned.Dispose();
        }
    }

    [Theory]
    [InlineData(ImageRotation.None, true, 1)]
    [InlineData(ImageRotation.Clockwise90, false, 1)]
    [InlineData(ImageRotation.CounterClockwise90, false, 1)]
    [InlineData(ImageRotation.Rotate180, true, 3)]
    public void LineExtractor_InCameraImage_NamesTheRowsOrColumnsTheLineCovers(ImageRotation rotation, bool isColumn, int start)
    {
        Assert.Equal((isColumn, start), LineExtractor.InCameraImage(new FinishLineSettings(rotation, 1, 2, false), cols: 6, rows: 4));
    }

    [Fact]
    public async Task FinishImageRenderer_DrawsColumnsInTimeDirectionWithTimeline()
    {
        var path = Path.Combine(Path.GetTempPath(), $"finish-{Guid.NewGuid():N}.png");
        var columns = Enumerable.Range(0, 100)
            .Select(i => new LineColumn(T0.AddSeconds(0.95 + (i / 50.0)), Enumerable.Repeat((byte)(i == 0 ? 255 : 0), 60 * 3).ToArray()))
            .ToList();
        var renderer = new FinishImageRenderer(TimeProvider.System);
        try
        {
            var forward = await renderer.RenderAsync(columns, false, path, Ct);
            using (var image = Cv2.ImRead(path))
            {
                Assert.Equal(new FinishImageInfo(100, 60 + FinishImageRenderer.TimelineHeight, FinishImageRenderer.TimelineHeight), forward);
                Assert.Equal(255, image.At<Vec3b>(10, 0).Item0);
                // Full second 10:00:01 is at column 3 (first column at or after it): a long mark.
                Assert.True(image.At<Vec3b>(60 + 10, 3).Item0 > 200);
            }

            await renderer.RenderAsync(columns, true, path, Ct);
            using var reversed = Cv2.ImRead(path);
            Assert.Equal(255, reversed.At<Vec3b>(10, 99).Item0);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task VideoEncoder_FfmpegMissing_ReturnsStableError()
    {
        var encoder = new FfmpegVideoEncoder(new CameraOptions { FfmpegPath = Path.Combine(Path.GetTempPath(), "no-such-ffmpeg.exe") }, NullLogger<FfmpegVideoEncoder>.Instance);

        var finish = await encoder.EncodeFinishVideoAsync("x.png", new FinishImageInfo(100, 100, 36), 90, false, "x.mp4", Ct);
        var front = await encoder.EncodeFrontVideoAsync([new EncodedFrame(T0, [1])], 30, "y.mp4", Ct);

        Assert.Equal("video.ffmpegMissing", finish.Error?.Code);
        Assert.Equal("video.ffmpegMissing", front.Error?.Code);
    }

    [Fact]
    public async Task VideoEncoder_ProcessFails_ReturnsEncodingFailed()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Uses a Windows system tool as a failing process.");
        var failing = Path.Combine(Environment.SystemDirectory, "where.exe");
        var encoder = new FfmpegVideoEncoder(new CameraOptions { FfmpegPath = failing }, NullLogger<FfmpegVideoEncoder>.Instance);
        var output = Path.Combine(Path.GetTempPath(), $"front-{Guid.NewGuid():N}.mp4");

        var result = await encoder.EncodeFrontVideoAsync([new EncodedFrame(T0, new byte[1000])], 30, output, Ct);

        Assert.Equal("video.encodingFailed", result.Error?.Code);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public void VideoEncoderArguments_UseInvariantNumbersAndBrowserCompatibleOutput()
    {
        var previous = CultureInfo.CurrentCulture;
        // Invariant globalization mode: a culture with a decimal comma stands in for e.g. de-DE.
        var comma = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        comma.NumberFormat.NumberDecimalSeparator = ",";
        CultureInfo.CurrentCulture = comma;
        try
        {
            var finish = FfmpegVideoEncoder.FinishVideoArguments("in.png", new FinishImageInfo(1001, 1117, 36), 89.5, false, "out.mp4");
            var front = FfmpegVideoEncoder.FrontVideoArguments(29.97, "out.mp4");

            Assert.Contains("89.5", finish);
            Assert.Contains("29.97", front);
            Assert.Contains("crop=640:1116:min(max(n-639\\,0)\\,361):0,format=yuv420p", finish);
            Assert.Contains("1001", finish);
            foreach (var arguments in new[] { finish, front })
            {
                Assert.Contains("libx264", arguments);
                Assert.Contains("yuv420p", arguments);
                Assert.Contains("+faststart", arguments);
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
    [Fact]
    public async Task Start_CameraSelectedByName_OpensTheDeviceAtItsCurrentIndex()
    {
        var sources = new FakeSourceFactory(_ => new FakeSource(320, 240, 60), simulated: false);
        var devices = new FakeDeviceEnumerator(new("Webcam", @"\\?\usb#a"), new("SVPRO", @"\\?\usb#b"), new("Front", @"\\?\usb#c"));
        using var system = System(sources, TimeProvider.System, devices: devices);
        var options = Options() with
        {
            FinishCamera = Options().FinishCamera with { DeviceIndex = 0, Device = new CameraDeviceRef("SVPRO", @"\\?\usb#b") },
            FrontCamera = Options().FrontCamera! with { DeviceIndex = 1, Device = new CameraDeviceRef("Front", null) },
        };

        var session = await system.StartAsync(options, Ct);
        await session.DisposeAsync();

        Assert.Equal(1, sources.Created[CameraRole.Finish].DeviceIndex);
        Assert.Equal(2, sources.Created[CameraRole.Front].DeviceIndex);
    }

    [Fact]
    public async Task Szenario_AusgewaehlteKameraFehlt_ReportsNotFoundAndOpensNoOtherCamera()
    {
        var sources = new FakeSourceFactory(_ => new FakeSource(320, 240, 60), simulated: false);
        var devices = new FakeDeviceEnumerator(new("SVPRO", @"\\?\usb#b"), new("Other webcam", @"\\?\usb#x"));
        using var system = System(sources, TimeProvider.System, devices: devices);
        var options = Options() with
        {
            FinishCamera = Options().FinishCamera with { Device = new CameraDeviceRef("SVPRO", @"\\?\usb#b") },
            FrontCamera = Options().FrontCamera! with { Device = new CameraDeviceRef("Front", @"\\?\usb#c") },
        };

        var session = await system.StartAsync(options, Ct);
        await ReadColumnsAsync(session, 3);
        await Task.Delay(100, Ct);

        Assert.Equal(new CameraHealth(CameraState.Error, "camera.notFound", 0), session.Health(CameraRole.Front));
        Assert.Equal(CameraState.Running, session.Health(CameraRole.Finish).State);
        Assert.False(sources.Created.ContainsKey(CameraRole.Front));
        await session.DisposeAsync();
    }

    [Fact]
    public async Task ListDevices_Simulation_ReportsSelectableSimulatedDevices()
    {
        using var system = System(new FakeSourceFactory(_ => new FakeSource(320, 240, 60)), TimeProvider.System);

        var devices = await system.ListDevicesAsync(Ct);

        Assert.Equal(["simulation:0", "simulation:1"], devices.Select(d => d.DevicePath));
        Assert.Equal(["Simulation 0", "Simulation 1"], devices.Select(d => d.DeviceName));
        Assert.All(devices, d => Assert.Contains(new CameraMode(1920, 1080, 90, "MJPG"), d.Modes));
    }

    private static readonly EnumeratedDevice[] TwoEqualCameras =
    [
        new("Webcam", null),
        new("SVPRO", @"\\?\usb#port1"),
        new("SVPRO", @"\\?\usb#port2"),
    ];

    public static TheoryData<CameraDeviceRef?, int, int?> Resolutions => new()
    {
        { null, 5, 5 },
        { new CameraDeviceRef("SVPRO", @"\\?\USB#PORT2"), 0, 2 },
        { new CameraDeviceRef("Webcam", @"\\?\usb#moved"), 7, 0 },
        { new CameraDeviceRef("Webcam", null), 7, 0 },
        { new CameraDeviceRef("SVPRO", @"\\?\usb#port3"), 1, null },
        { new CameraDeviceRef("SVPRO", null), 1, null },
        { new CameraDeviceRef("Front", null), 1, null },
    };

    [Theory]
    [MemberData(nameof(Resolutions))]
    public void IndexOf_SelectedDevice_ResolvesByPathThenUniqueNameElseNotFound(CameraDeviceRef? device, int configuredIndex, int? expected)
    {
        var settings = Defaults.FinishCamera with { DeviceIndex = configuredIndex, Device = device };

        var index = DeviceResolution.IndexOf(settings, TwoEqualCameras);

        Assert.Equal(expected, index);
    }

    [Fact]
    public void Describe_DeviceWithDriverName_IsSelectableByNameOtherwiseByIndex()
    {
        CameraMode[] modes = [new(1920, 1080, 90, "MJPG")];
        EnumeratedDevice[] devices = [new("SVPRO USB Camera", @"\\?\usb#a") { Modes = modes }, new("", null)];

        var named = CameraSystem.Describe(0, devices, inUse: true);
        var unnamed = CameraSystem.Describe(1, devices, inUse: false);
        var unknown = CameraSystem.Describe(2, devices, inUse: false);

        Assert.Equal((0, "SVPRO USB Camera", true, "SVPRO USB Camera", @"\\?\usb#a"), (named.Index, named.Name, named.InUse, named.DeviceName, named.DevicePath));
        Assert.Equal(modes, named.Modes);
        Assert.Equal(("USB 1", null, null), (unnamed.Name, unnamed.DeviceName, unnamed.DevicePath));
        Assert.Equal(("USB 2", 0), (unknown.Name, unknown.Modes.Count));
    }

    [Fact]
    public void SelectModes_PrefersConfiguredFormatAndKeepsHighestRatePerSize()
    {
        CameraMode[] modes =
        [
            new(1280, 720, 30, "MJPG"),
            new(1920, 1080, 60, "MJPG"),
            new(1920, 1080, 90, "MJPG"),
            new(1920, 1080, 5, "YUY2"),
            new(0, 0, 30, "MJPG"),
        ];

        var selected = DirectShowModes.Select(modes, "mjpg");

        Assert.Equal([new CameraMode(1920, 1080, 90, "MJPG"), new CameraMode(1280, 720, 30, "MJPG")], selected);
    }

    [Fact]
    public void SelectModes_CameraWithoutConfiguredFormat_OffersAllFormats()
    {
        CameraMode[] modes = [new(640, 480, 30, "YUY2"), new(1280, 720, 10, "NV12")];

        var selected = DirectShowModes.Select(modes, "MJPG");

        Assert.Equal([new CameraMode(1280, 720, 10, "NV12"), new CameraMode(640, 480, 30, "YUY2")], selected);
    }

    [Theory]
    [InlineData(CaptureBackend.Auto)]
    [InlineData(CaptureBackend.Msmf)]
    [InlineData(CaptureBackend.V4L2)]
    public void DirectShowDevices_BackendWithOtherIndexOrder_ReturnsNoDevices(CaptureBackend backend)
    {
        var devices = new DirectShowDevices(new CameraOptions { Backend = backend }, NullLogger<DirectShowDevices>.Instance);

        Assert.Empty(devices.List());
    }

    [Theory]
    [InlineData(CaptureBackend.DShow)]
    [InlineData(CaptureBackend.V4L2)]
    public void MediaFoundationDevices_BackendWithOtherIndexOrder_ReturnsNoDevices(CaptureBackend backend)
    {
        var devices = new MediaFoundationDevices(new CameraOptions { Backend = backend }, NullLogger<MediaFoundationDevices>.Instance);

        Assert.Empty(devices.List());
    }

    [Fact]
    public void DeviceEnumerators_OnThisMachine_DoNotThrow()
    {
        var directShow = new DirectShowDevices(new CameraOptions { Backend = CaptureBackend.DShow }, NullLogger<DirectShowDevices>.Instance);
        var mediaFoundation = new MediaFoundationDevices(new CameraOptions { Backend = CaptureBackend.Msmf }, NullLogger<MediaFoundationDevices>.Instance);

        var lists = new[] { directShow.List(), mediaFoundation.List(), mediaFoundation.List(includeModes: false) };

        Assert.All(lists.SelectMany(l => l), device => Assert.NotNull(device.Name));
        Assert.All(lists[1], device => Assert.NotEmpty(device.Modes));
        Assert.All(lists[2], device => Assert.Empty(device.Modes));
    }

    [Fact]
    public void IndexOf_PathSavedWithOtherBackend_MatchesTheSameCamera()
    {
        // The same camera as reported by DirectShow (saved) and Media Foundation (current).
        var saved = new CameraDeviceRef("SVPRO", @"\\?\usb#vid_32e4&pid_0234&mi_00#9&325db0ba&0&0000#{65e8773d-8f56-11d0-a3b9-00a0c9223196}\global");
        EnumeratedDevice[] devices =
        [
            new("SVPRO", @"\\?\usb#vid_32e4&pid_0234&mi_00#9&aaaaaaa&0&0000#{e5323777-f976-4f5b-9b55-b94699c46e44}\global"),
            new("SVPRO", @"\\?\USB#VID_32E4&PID_0234&MI_00#9&325DB0BA&0&0000#{e5323777-f976-4f5b-9b55-b94699c46e44}\global"),
        ];

        var index = DeviceResolution.IndexOf(Defaults.FinishCamera with { Device = saved }, devices);

        Assert.Equal(1, index);
    }

    [Fact]
    public async Task Health_ExposureNotApplied_IsReportedAsWarningWhileTheCameraRuns()
    {
        var sources = new FakeSourceFactory(role => new FakeSource(320, 240, 60, warning: role == CameraRole.Finish ? "camera.exposureNotApplied" : null));
        using var system = System(sources, TimeProvider.System);

        var session = await system.StartAsync(Options(), Ct);
        await ReadColumnsAsync(session, 3);
        var finish = session.Health(CameraRole.Finish);
        var front = session.Health(CameraRole.Front);
        await session.DisposeAsync();

        Assert.Equal((CameraState.Running, "camera.exposureNotApplied"), (finish.State, finish.WarningCode));
        Assert.Null(front.WarningCode);
    }

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void MediaFoundationCameraSource_UnknownDevice_FailsToOpen()
    {
        var settings = Defaults.FinishCamera with { DeviceIndex = CameraSettings.MaxDeviceIndex };
        using var source = new MediaFoundationCameraSource(settings, new CameraOptions());

        Assert.Throws<CameraOpenException>(source.Open);
    }

    [Theory]
    [InlineData(0x0000005A_00000001UL, 90)]
    [InlineData(0x00007530_000003E9UL, 29.97)]
    [InlineData(0x0000001E_00000000UL, 0)]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void FrameRate_PackedRatio_IsNumeratorOverDenominator(ulong packed, double expected) =>
        Assert.Equal(expected, MediaFoundationCameraSource.FrameRate(packed));

    [Theory]
    [InlineData(CaptureBackend.Auto, true)]
    [InlineData(CaptureBackend.Msmf, true)]
    [InlineData(CaptureBackend.DShow, false)]
    [InlineData(CaptureBackend.V4L2, false)]
    public void UsesMediaFoundation_OnWindows_ForAutoAndMsmf(CaptureBackend backend, bool expected) =>
        Assert.Equal(expected && OperatingSystem.IsWindows(), new CameraOptions { Backend = backend }.UsesMediaFoundation);

    [Fact]
    public void LiveStripSlots_IrregularColumns_HoldTheNewestColumnPerFixedTimeSlotAndShowGaps()
    {
        LineColumn[] columns = [Column(0), Column(50), Column(250), Column(400)];

        var slots = LiveStrip.Slots(columns, T0.AddMilliseconds(400), rate: 10, count: 5, LiveStrip.MaxHold);

        Assert.Equal([0, 1, -1, 2, 3], slots);
    }

    [Theory]
    [InlineData(90)]
    [InlineData(45)]
    [InlineData(120)]
    public void LiveStrip_FluctuatingFrameRate_KeepsAFixedImageSize(double actualRate)
    {
        var strip = new LiveStrip(TimeSpan.FromSeconds(5), frameRate: 90, reverse: false, jpegQuality: 80, TimeZoneInfo.Utc);
        for (var i = 0; i < actualRate * 6; i++)
        {
            strip.Add(Column(i * 1000 / actualRate));
        }

        using var image = Cv2.ImDecode(strip.LatestJpeg()!, ImreadModes.Color);

        Assert.Equal((900, 276), (image.Width, image.Height));
    }

    private static LineColumn Column(double milliseconds) => new(T0.AddMilliseconds(milliseconds), new byte[720 * 3]);

    [Fact]
    public async Task Session_CameraJpegs_AreDecodedInParallelAndEmittedInCaptureOrder()
    {
        using var system = System(new FakeSourceFactory(_ => new FakeJpegSource(320, 240)), TimeProvider.System);

        var session = await system.StartAsync(Options(front: false), Ct);
        var columns = await ReadColumnsAsync(session, 50);
        await session.DisposeAsync();

        Assert.All(columns.Zip(columns.Skip(1)), pair => Assert.True(pair.First.Timestamp < pair.Second.Timestamp));
        var grays = columns.Select(c => (int)c.Bgr[0]).ToList();
        Assert.All(grays.Zip(grays.Skip(1)), pair => Assert.True(pair.Second > pair.First || pair.Second < 20, $"{pair.First} -> {pair.Second}"));
        Assert.Equal(240 * 3, columns[0].Bgr.Length);
    }

    [Fact]
    public async Task Session_FrontCameraJpegs_AreBufferedWithoutReencoding()
    {
        var front = new FakeJpegSource(320, 240);
        using var system = System(new FakeSourceFactory(role => role == CameraRole.Front ? front : new FakeSource(320, 240, 50)), TimeProvider.System);

        var session = await system.StartAsync(Options(), Ct);
        await session.FrontFrames!.WaitForAsync(DateTimeOffset.MinValue, TimeSpan.FromSeconds(5), Ct);
        await Task.Delay(100, Ct);
        await session.DisposeAsync();

        var buffered = session.FrontFrames.Snapshot(DateTimeOffset.MinValue, DateTimeOffset.MaxValue);
        Assert.NotEmpty(buffered);
        lock (front.Produced)
        {
            Assert.All(buffered, frame => Assert.Contains(front.Produced, jpeg => ReferenceEquals(jpeg, frame.Jpeg)));
        }
    }
}
