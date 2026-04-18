using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace StockifhsGUI;

public partial class MainForm
{
    private Button? startTrackingButton;
    private Button? stopTrackingButton;
    private Label? trackingStatusLabel;
    private CheckBox? alwaysOnTopCheckBox;
    private System.Windows.Forms.Timer? trackingTimer;

    private void InitializeTrackingControls()
    {
        startTrackingButton = new Button
        {
            Text = "Start Tracking",
            Size = new Size(120, 32)
        };
        startTrackingButton.Click += async (_, _) => await StartTrackingAsync();
        Controls.Add(startTrackingButton);

        stopTrackingButton = new Button
        {
            Text = "Stop Tracking",
            Size = new Size(120, 32),
            Enabled = false
        };
        stopTrackingButton.Click += (_, _) => StopTracking("Tracking stopped.");
        Controls.Add(stopTrackingButton);

        alwaysOnTopCheckBox = new CheckBox
        {
            AutoSize = true,
            Text = "Always on top"
        };
        alwaysOnTopCheckBox.CheckedChanged += (_, _) => TopMost = alwaysOnTopCheckBox.Checked;
        Controls.Add(alwaysOnTopCheckBox);

        trackingStatusLabel = new Label
        {
            AutoSize = false,
            Size = new Size(260, 72),
            Text = "Tracking: idle"
        };
        Controls.Add(trackingStatusLabel);

        trackingTimer = new System.Windows.Forms.Timer
        {
            Interval = 500
        };
        trackingTimer.Tick += async (_, _) => await PollTrackingAsync();
    }

    private async Task StartTrackingAsync()
    {
        bool restoreTopMost = alwaysOnTopCheckBox?.Checked == true;
        TopMost = false;

        using TrackingSetupForm setupForm = new();
        DialogResult result = setupForm.ShowDialog(this);

        TopMost = restoreTopMost;

        if (result != DialogResult.OK || setupForm.TrackingProfile is null)
        {
            SetTrackingStatus("Tracking: setup canceled");
            return;
        }

        trackingTimer?.Start();
        await trackingWorkflow.StartAsync(setupForm.TrackingProfile);
    }

    private void StopTracking(string message)
    {
        trackingTimer?.Stop();
        trackingWorkflow.Stop(message);
    }

    private async Task PollTrackingAsync()
    {
        await trackingWorkflow.PollAsync();
    }

    private bool TryLoadTrackedSnapshot(TrackedPositionSnapshot snapshot, out string? error)
    {
        error = null;

        if (!FenPosition.TryParse(snapshot.Fen, out FenPosition? position, out error) || position is null)
        {
            return false;
        }

        suppressEngineRefresh = true;
        try
        {
            undoStack.Clear();
            analysisArrows.Clear();
            analysisTargetSquare = null;
            moveHistory.Clear();
            ApplyPosition(position);
            importedSession.LoadTrackedMoves(snapshot.Moves);
            PopulateImportedMovesList();

            ClearSelection();
        }
        finally
        {
            suppressEngineRefresh = false;
        }

        RefreshEngineSuggestions();
        UpdateExtendedControls();
        InvalidateBoardSurface();
        return true;
    }

    private string GetCurrentFen()
    {
        return FenPosition.FromBoardState(
            board,
            whiteToMove,
            whiteKingMoved,
            blackKingMoved,
            whiteRookLeftMoved,
            whiteRookRightMoved,
            blackRookLeftMoved,
            blackRookRightMoved,
            enPassantTargetSquare,
            halfmoveClock,
            fullmoveNumber).GetFen();
    }

    private void SetTrackingStatus(string message)
    {
        if (trackingStatusLabel is not null)
        {
            trackingStatusLabel.Text = message;
        }
    }

    private void SetTrackingControlsRunning(bool isRunning)
    {
        if (startTrackingButton is not null)
        {
            startTrackingButton.Enabled = !isRunning;
        }

        if (stopTrackingButton is not null)
        {
            stopTrackingButton.Enabled = isRunning;
        }
    }

    string ITrackingWorkflowHost.GetCurrentFen() => GetCurrentFen();

    string ITrackingWorkflowHost.GetTesseractDataPath() => TesseractDataResolver.GetExpectedDataPath();

    bool ITrackingWorkflowHost.TryLoadTrackedSnapshot(TrackedPositionSnapshot snapshot, out string? error)
        => TryLoadTrackedSnapshot(snapshot, out error);

    void ITrackingWorkflowHost.SetTrackingStatus(string message) => SetTrackingStatus(message);

    void ITrackingWorkflowHost.SetTrackingControlsRunning(bool isRunning) => SetTrackingControlsRunning(isRunning);
}
