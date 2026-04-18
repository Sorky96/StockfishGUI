using System;
using System.Threading.Tasks;

namespace StockifhsGUI;

internal sealed class TrackingWorkflowCoordinator
{
    private readonly ITrackingWorkflowHost host;
    private Tracker? tracker;
    private TrackingProfile? trackingProfile;
    private bool trackingPollInProgress;
    private string? lastTrackedFen;

    public TrackingWorkflowCoordinator(ITrackingWorkflowHost host)
    {
        this.host = host;
    }

    public async Task StartAsync(TrackingProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        trackingProfile = profile;
        tracker ??= new Tracker(host.GetTesseractDataPath());
        lastTrackedFen = null;

        host.SetTrackingControlsRunning(isRunning: true);

        if (trackingProfile.BoardOnly)
        {
            TrackingUpdate initialBoardUpdate = tracker.InitializeBoardOnly(trackingProfile, host.GetCurrentFen());
            ApplyTrackingUpdate(initialBoardUpdate);
        }

        host.SetTrackingStatus($"Tracking: {trackingProfile.WindowTitle}");
        await PollAsync();
    }

    public void Stop(string message)
    {
        trackingProfile = null;
        trackingPollInProgress = false;
        lastTrackedFen = null;
        host.SetTrackingControlsRunning(isRunning: false);
        host.SetTrackingStatus(message);
    }

    public async Task PollAsync()
    {
        if (trackingPollInProgress || tracker is null || trackingProfile is null)
        {
            return;
        }

        trackingPollInProgress = true;
        try
        {
            TrackingProfile profile = trackingProfile;
            TrackingUpdate update = await Task.Run(() => tracker.Poll(profile));
            ApplyTrackingUpdate(update);
        }
        catch (Exception ex)
        {
            if (IsTransientTrackingException(ex))
            {
                host.SetTrackingStatus($"Tracking temporarily unavailable: {ex.Message}");
                return;
            }

            Stop($"Tracking error: {ex.Message}");
        }
        finally
        {
            trackingPollInProgress = false;
        }
    }

    private void ApplyTrackingUpdate(TrackingUpdate update)
    {
        if (update.Snapshot is not null && update.Snapshot.Fen != lastTrackedFen)
        {
            if (!host.TryLoadTrackedSnapshot(update.Snapshot, out string? error))
            {
                host.SetTrackingStatus($"Tracking import failed: {error}");
                return;
            }

            lastTrackedFen = update.Snapshot.Fen;
        }

        host.SetTrackingStatus(update.Status.Message);
    }

    private static bool IsTransientTrackingException(Exception ex)
    {
        return ex is ObjectDisposedException
            || ex is InvalidOperationException
            || ex.Message.Contains("zamykanie", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("closed", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("disposed", StringComparison.OrdinalIgnoreCase);
    }
}
