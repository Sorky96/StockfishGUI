using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace StockifhsGUI;

public sealed class TrackingSetupForm : Form
{
    private readonly Label windowLabel;
    private readonly Label boardRegionLabel;
    private readonly Label moveRegionLabel;
    private readonly CheckBox whiteAtBottomCheckBox;
    private readonly CheckBox boardOnlyCheckBox;
    private readonly Button okButton;
    private bool captureInProgress;

    private WindowCaptureInfo? capturedWindow;
    private Rectangle? boardRegion;
    private Rectangle? moveRegion;

    public TrackingSetupForm()
    {
        Text = "Tracking Setup";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(470, 280);

        Label introLabel = new()
        {
            AutoSize = false,
            Location = new Point(16, 12),
            Size = new Size(438, 56),
            Text = "Capture the Chess.com window, then mark the board and move-list regions. The tracker will poll those areas every 500 ms. In Board only mode, the app will first detect the current piece layout directly from the board image."
        };
        Controls.Add(introLabel);

        windowLabel = new Label
        {
            AutoSize = false,
            Location = new Point(16, 64),
            Size = new Size(438, 22),
            Text = "Window: not captured"
        };
        Controls.Add(windowLabel);

        Button captureWindowButton = new()
        {
            Text = "Capture Active Window (3s)",
            Location = new Point(16, 92),
            Size = new Size(210, 32)
        };
        captureWindowButton.Click += async (_, _) => await CaptureActiveWindowAsync();
        Controls.Add(captureWindowButton);

        boardRegionLabel = new Label
        {
            AutoSize = false,
            Location = new Point(16, 136),
            Size = new Size(438, 22),
            Text = "Board region: not selected"
        };
        Controls.Add(boardRegionLabel);

        Button boardRegionButton = new()
        {
            Text = "Select Board Region",
            Location = new Point(16, 164),
            Size = new Size(180, 32)
        };
        boardRegionButton.Click += (_, _) => SelectRegion(
            "Drag a rectangle around the Chess.com board. Release the mouse to confirm.",
            rectangle =>
            {
                boardRegion = rectangle;
                boardRegionLabel.Text = $"Board region: {FormatRectangle(rectangle)}";
                UpdateOkState();
            });
        Controls.Add(boardRegionButton);

        moveRegionLabel = new Label
        {
            AutoSize = false,
            Location = new Point(16, 204),
            Size = new Size(438, 22),
            Text = "Move list region: not selected"
        };
        Controls.Add(moveRegionLabel);

        Button moveRegionButton = new()
        {
            Text = "Select Move List Region",
            Location = new Point(16, 232),
            Size = new Size(180, 32)
        };
        moveRegionButton.Click += (_, _) => SelectRegion(
            "Drag a rectangle around the Chess.com move list. Release the mouse to confirm.",
            rectangle =>
            {
                moveRegion = rectangle;
                moveRegionLabel.Text = $"Move list region: {FormatRectangle(rectangle)}";
                UpdateOkState();
            });
        Controls.Add(moveRegionButton);

        whiteAtBottomCheckBox = new CheckBox
        {
            AutoSize = true,
            Location = new Point(220, 170),
            Text = "White pieces are at the bottom",
            Checked = true
        };
        Controls.Add(whiteAtBottomCheckBox);

        boardOnlyCheckBox = new CheckBox
        {
            AutoSize = true,
            Location = new Point(220, 194),
            Text = "Board only (ignore move list OCR)"
        };
        boardOnlyCheckBox.CheckedChanged += (_, _) =>
        {
            moveRegionLabel.Enabled = !boardOnlyCheckBox.Checked;
            moveRegionButton.Enabled = !boardOnlyCheckBox.Checked;
            UpdateOkState();
        };
        Controls.Add(boardOnlyCheckBox);

        okButton = new Button
        {
            Text = "Start Tracking",
            Location = new Point(310, 232),
            Size = new Size(144, 32),
            Enabled = false
        };
        okButton.Click += (_, _) =>
        {
            if (capturedWindow is null || boardRegion is null || (!boardOnlyCheckBox.Checked && moveRegion is null))
            {
                return;
            }

            TrackingProfile = new TrackingProfile(
                capturedWindow.Handle,
                capturedWindow.Title,
                boardRegion.Value,
                moveRegion ?? Rectangle.Empty,
                whiteAtBottomCheckBox.Checked,
                boardOnlyCheckBox.Checked);
            DialogResult = DialogResult.OK;
            Close();
        };
        Controls.Add(okButton);
    }

    public TrackingProfile? TrackingProfile { get; private set; }

    private async Task CaptureActiveWindowAsync()
    {
        if (captureInProgress)
        {
            return;
        }

        captureInProgress = true;
        bool previousEnabledState = Enabled;
        FormWindowState previousWindowState = WindowState;

        try
        {
            Enabled = false;
            WindowState = FormWindowState.Minimized;
            await Task.Delay(3000);

            capturedWindow = NativeMethods.TryGetForegroundWindowInfo();

            if (IsDisposed || Disposing)
            {
                return;
            }

            WindowState = previousWindowState;
            Enabled = previousEnabledState;
            Activate();
        }
        finally
        {
            if (!IsDisposed && !Disposing)
            {
                WindowState = previousWindowState;
                Enabled = previousEnabledState;
            }

            captureInProgress = false;
        }

        windowLabel.Text = capturedWindow is null
            ? "Window: capture failed"
            : $"Window: {capturedWindow.Title}";
        UpdateOkState();
    }

    private void SelectRegion(string prompt, Action<Rectangle> onSelected)
    {
        FormWindowState previousWindowState = WindowState;
        bool previousEnabledState = Enabled;
        Enabled = false;
        WindowState = FormWindowState.Minimized;

        using RegionSelectionOverlayForm overlay = new(prompt);
        if (overlay.ShowDialog() == DialogResult.OK)
        {
            onSelected(overlay.SelectedRegion);
        }

        if (IsDisposed || Disposing)
        {
            return;
        }

        WindowState = previousWindowState;
        Enabled = previousEnabledState;
        Activate();
    }

    private void UpdateOkState()
    {
        okButton.Enabled = capturedWindow is not null
            && boardRegion is not null
            && (boardOnlyCheckBox.Checked || moveRegion is not null);
    }

    private static string FormatRectangle(Rectangle rectangle)
    {
        return $"X={rectangle.X}, Y={rectangle.Y}, W={rectangle.Width}, H={rectangle.Height}";
    }
}
