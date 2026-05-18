using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;

namespace MoveMentorChess.Tracking;

public sealed class Tracker
{
    private readonly ScreenCaptureService captureService;
    private readonly TrackingCoordinator coordinator;

    public Tracker(string tesseractDataPath)
        : this(tesseractDataPath, SystemClock.Instance)
    {
    }

    public Tracker(string tesseractDataPath, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        captureService = new ScreenCaptureService();
        coordinator = new TrackingCoordinator(
            new MoveListOcrRecognizer(tesseractDataPath, clock),
            new BoardPositionRecognizer(Path.Combine(AppContext.BaseDirectory, "Images")),
            captureService,
            clock);
    }

    public TrackingUpdate Poll(TrackingProfile profile)
    {
        if (!NativeMethods.WindowExists(profile.WindowHandle))
        {
            return new TrackingUpdate(
                new TrackerStatus(TrackerStatusKind.Unsupported, $"Tracked window '{profile.WindowTitle}' is no longer available."),
                null);
        }

        try
        {
            using Bitmap boardImage = captureService.Capture(profile.BoardRegion);
            if (profile.BoardOnly)
            {
                return coordinator.ProcessFrame(boardImage, boardImage, profile);
            }

            using Bitmap moveListImage = captureService.Capture(profile.MoveListRegion);
            return coordinator.ProcessFrame(boardImage, moveListImage, profile);
        }
        catch (Exception ex) when (IsTransientCaptureException(ex))
        {
            return new TrackingUpdate(
                new TrackerStatus(
                    TrackerStatusKind.WaitingForStableFrame,
                    $"Tracking temporarily unavailable: {ex.Message}"),
                null);
        }
    }

    public TrackingUpdate InitializeBoardOnly(TrackingProfile profile, string metadataSeedFen)
    {
        using Bitmap boardImage = captureService.Capture(profile.BoardRegion);
        return coordinator.InitializeBoardOnly(boardImage, metadataSeedFen, profile.WhiteAtBottom);
    }

    private static bool IsTransientCaptureException(Exception ex)
    {
        return ex is ExternalException
            || ex is InvalidOperationException
            || ex is ObjectDisposedException
            || ex.Message.Contains("zamykanie", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("closed", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("disposed", StringComparison.OrdinalIgnoreCase);
    }
}
