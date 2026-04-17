using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace StockifhsGUI;

public partial class Form1
{
    private Button? startTrackingButton;
    private Button? stopTrackingButton;
    private Label? trackingStatusLabel;
    private CheckBox? alwaysOnTopCheckBox;
    private System.Windows.Forms.Timer? trackingTimer;
    private Tracker? tracker;
    private TrackingProfile? trackingProfile;
    private bool trackingPollInProgress;
    private string? lastTrackedFen;

    private void InitializeTrackingControls()
    {
        startTrackingButton = new Button
        {
            Text = "Start Tracking",
            Location = new Point(TileSize * GridSize + 20, 408),
            Size = new Size(120, 32)
        };
        startTrackingButton.Click += async (_, _) => await StartTrackingAsync();
        Controls.Add(startTrackingButton);

        stopTrackingButton = new Button
        {
            Text = "Stop Tracking",
            Location = new Point(TileSize * GridSize + 150, 408),
            Size = new Size(120, 32),
            Enabled = false
        };
        stopTrackingButton.Click += (_, _) => StopTracking("Tracking stopped.");
        Controls.Add(stopTrackingButton);

        alwaysOnTopCheckBox = new CheckBox
        {
            AutoSize = true,
            Location = new Point(TileSize * GridSize + 20, 450),
            Text = "Always on top"
        };
        alwaysOnTopCheckBox.CheckedChanged += (_, _) => TopMost = alwaysOnTopCheckBox.Checked;
        Controls.Add(alwaysOnTopCheckBox);

        trackingStatusLabel = new Label
        {
            AutoSize = false,
            Location = new Point(TileSize * GridSize + 20, 476),
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

        trackingProfile = setupForm.TrackingProfile;

        tracker ??= new Tracker(TesseractDataResolver.GetExpectedDataPath());
        lastTrackedFen = null;

        if (startTrackingButton is not null)
        {
            startTrackingButton.Enabled = false;
        }

        if (stopTrackingButton is not null)
        {
            stopTrackingButton.Enabled = true;
        }

        if (trackingProfile.BoardOnly)
        {
            TrackingUpdate initialBoardUpdate = tracker.InitializeBoardOnly(trackingProfile, GetCurrentFen());
            if (initialBoardUpdate.Snapshot is not null)
            {
                ApplyTrackingUpdate(initialBoardUpdate);
            }
            else
            {
                SetTrackingStatus(initialBoardUpdate.Status.Message);
            }
        }

        SetTrackingStatus($"Tracking: {trackingProfile.WindowTitle}");
        trackingTimer?.Start();
        await PollTrackingAsync();
    }

    private void StopTracking(string message)
    {
        trackingTimer?.Stop();
        trackingProfile = null;
        trackingPollInProgress = false;
        lastTrackedFen = null;

        if (startTrackingButton is not null)
        {
            startTrackingButton.Enabled = true;
        }

        if (stopTrackingButton is not null)
        {
            stopTrackingButton.Enabled = false;
        }

        SetTrackingStatus(message);
    }

    private async Task PollTrackingAsync()
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
                SetTrackingStatus($"Tracking temporarily unavailable: {ex.Message}");
                return;
            }

            StopTracking($"Tracking error: {ex.Message}");
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
            if (!TryLoadTrackedSnapshot(update.Snapshot, out string? error))
            {
                SetTrackingStatus($"Tracking import failed: {error}");
                return;
            }

            lastTrackedFen = update.Snapshot.Fen;
        }

        SetTrackingStatus(update.Status.Message);
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
            importedGame = null;
            importedMoves.Clear();
            importedMovesList?.Items.Clear();
            for (int x = 0; x < GridSize; x++)
            {
                for (int y = 0; y < GridSize; y++)
                {
                    board[x, y] = position.Board[x, y];
                }
            }

            moveHistory.Clear();
            whiteToMove = position.WhiteToMove;
            whiteKingMoved = position.WhiteKingMoved;
            blackKingMoved = position.BlackKingMoved;
            whiteRookLeftMoved = position.WhiteRookLeftMoved;
            whiteRookRightMoved = position.WhiteRookRightMoved;
            blackRookLeftMoved = position.BlackRookLeftMoved;
            blackRookRightMoved = position.BlackRookRightMoved;
            importedMoveCursor = snapshot.Moves.Count;

            if (importedMovesList is not null)
            {
                suppressImportedSelectionHandling = true;
                for (int i = 0; i < snapshot.Moves.Count; i++)
                {
                    int ply = i + 1;
                    PlayerSide side = ply % 2 == 1 ? PlayerSide.White : PlayerSide.Black;
                    int moveNumber = (ply + 1) / 2;
                    ImportedMove move = new(ply, moveNumber, side, snapshot.Moves[i]);
                    importedMoves.Add(move);
                    importedMovesList.Items.Add(move);
                }
                suppressImportedSelectionHandling = false;
            }

            ClearSelection();
        }
        finally
        {
            suppressEngineRefresh = false;
        }

        RefreshEngineSuggestions();
        UpdateExtendedControls();
        Invalidate();
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
            null,
            0,
            Math.Max(1, moveHistory.Count / 2 + 1)).GetFen();
    }

    private void SetTrackingStatus(string message)
    {
        if (trackingStatusLabel is not null)
        {
            trackingStatusLabel.Text = message;
        }
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
