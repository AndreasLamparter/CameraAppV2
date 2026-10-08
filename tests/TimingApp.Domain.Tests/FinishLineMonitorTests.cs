using TimingApp.Domain.FinishRecording;

namespace TimingApp.Domain.Tests;

public sealed class FinishLineMonitorTests
{
    private static readonly DetectionSettings Settings = FinishRecordingSettings.Default.Detection;

    internal static byte[] Column(int length, byte gray, int occupiedFrom = 0, int occupiedCount = 0, byte riderGray = 0)
    {
        var bgr = new byte[length * 3];
        for (var i = 0; i < length; i++)
        {
            var value = i >= occupiedFrom && i < occupiedFrom + occupiedCount ? riderGray : gray;
            bgr[i * 3] = value;
            bgr[(i * 3) + 1] = value;
            bgr[(i * 3) + 2] = value;
        }
        return bgr;
    }

    private static FinishLineMonitor Learned(byte gray = 100)
    {
        var monitor = new FinishLineMonitor(Settings);
        for (var i = 0; i < FinishLineMonitor.LearningColumns; i++)
        {
            monitor.Measure(Column(100, gray), false);
        }
        return monitor;
    }

    [Fact]
    public void Measure_WhileLearning_ReportsZeroAndIsLearning()
    {
        var monitor = new FinishLineMonitor(Settings);

        var occupancy = monitor.Measure(Column(100, 100, 0, 100, 10), false);

        Assert.Equal(0, occupancy);
        Assert.True(monitor.IsLearning);
    }

    [Fact]
    public void Measure_RiderCoversPartOfTheLine_ReturnsFractionOfChangedPixels()
    {
        var monitor = Learned();

        var occupancy = monitor.Measure(Column(100, 100, 10, 25, 20), false);

        Assert.Equal(0.25, occupancy, 3);
        Assert.False(monitor.IsLearning);
    }

    [Fact]
    public void Measure_ChangeNotAbovePixelThreshold_IsNotCounted()
    {
        var monitor = Learned();

        var occupancy = monitor.Measure(Column(100, 100 + 25), false);

        Assert.Equal(0, occupancy);
    }

    [Fact]
    public void Measure_AfterRiderLeft_LineIsFreeAgain()
    {
        var monitor = Learned();
        for (var i = 0; i < 180; i++)
        {
            monitor.Measure(Column(100, 100, 0, 50, 20), false);
        }

        var occupancy = monitor.Measure(Column(100, 100), false);

        Assert.Equal(0, occupancy);
    }

    [Fact]
    public void Measure_SlowLightChangeWhileFree_IsFollowedByTheBackground()
    {
        var monitor = Learned(100);
        byte gray = 100;
        for (var step = 0; step < 60; step++)
        {
            gray++;
            for (var i = 0; i < 20; i++)
            {
                monitor.Measure(Column(100, gray), false);
            }
        }

        var occupancy = monitor.Measure(Column(100, gray), false);

        Assert.Equal(160, gray);
        Assert.Equal(0, occupancy);
    }

    [Fact]
    public void Measure_BackgroundHeld_DoesNotAdapt()
    {
        var monitor = Learned(100);
        for (var i = 0; i < 500; i++)
        {
            monitor.Measure(Column(100, 120), holdBackground: true);
        }

        var occupancy = monitor.Measure(Column(100, 130), false);

        Assert.Equal(1.0, occupancy);
    }

    [Fact]
    public void Measure_RiderResemblingBackgroundWhileHeld_DoesNotLockTheLineOccupied()
    {
        var monitor = Learned(100);
        for (var i = 0; i < 180; i++)
        {
            // Every other column the rider differs by less than the pixel threshold: measured as free.
            var rider = i % 2 == 0 ? (byte)40 : (byte)80;
            monitor.Measure(Column(100, 100, 0, 50, rider), holdBackground: true);
        }

        Assert.Equal(0, monitor.Measure(Column(100, 100), false));
    }

    [Fact]
    public void Relearn_StartsLearningAgain_WithTheCurrentLine()
    {
        var monitor = Learned(100);

        monitor.Relearn();
        for (var i = 0; i < FinishLineMonitor.LearningColumns; i++)
        {
            monitor.Measure(Column(100, 200), false);
        }

        Assert.False(monitor.IsLearning);
        Assert.Equal(0, monitor.Measure(Column(100, 200), false));
    }
}
