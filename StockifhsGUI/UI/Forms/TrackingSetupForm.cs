using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using MaterialSkin.Controls;

namespace StockifhsGUI;

public sealed class TrackingSetupForm : MaterialForm
{
    private readonly MaterialLabel windowLabel;
    private readonly MaterialLabel boardRegionLabel;
    private readonly MaterialLabel moveRegionLabel;
    private readonly MaterialCheckbox whiteAtBottomCheckBox;
    private readonly MaterialCheckbox boardOnlyCheckBox;
    private readonly MaterialButton okButton;
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

        MaterialLabel introLabel = new()
        {
            AutoSize = false,
            Location = new Point(16, 72),
            Size = new Size(438, 56),
            Text = "Capture the Chess.com window, then mark the board and move-list regions. The tracker will poll those areas every 500 ms. In Board only mode, the app will first detect the current piece layout directly from the board image."
        };
        Controls.Add(introLabel);

        windowLabel = new MaterialLabel
        {
            AutoSize = false,
            Location = new Point(16, 128),
            Size = new Size(438, 22),
            Text = "Window: not captured"
        };
        Controls.Add(windowLabel);

        MaterialButton captureWindowButton = new()
        {
            Text = "Capture Active Window (3s)",
            Location = new Point(16, 156),
            AutoSize = false,
            Size = new Size(210, 36)
        };
        captureWindowButton.Click += async (_, _) => await CaptureActiveWindowAsync();
        Controls.Add(captureWindowButton);

        boardRegionLabel = new MaterialLabel
        {
            AutoSize = false,
            Location = new Point(16, 200),
            Size = new Size(438, 22),
            Text = "Board region: not selected"
        };
        Controls.Add(boardRegionLabel);

        MaterialButton boardRegionButton = new()
        {
            Text = "Select Board Region",
            Location = new Point(16, 228),
            AutoSize = false,
            Size = new Size(180, 36)
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

        moveRegionLabel = new MaterialLabel
        {
            AutoSize = false,
            Location = new Point(16, 268),
            Size = new Size(438, 22),
            Text = "Move list region: not selected"
        };
        Controls.Add(moveRegionLabel);

        MaterialButton moveRegionButton = new()
        {
            Text = "Select Move List",
            Location = new Point(16, 296),
            AutoSize = false,
            Size = new Size(180, 36)
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

        whiteAtBottomCheckBox = new MaterialCheckbox
        {
            AutoSize = true,
            Location = new Point(220, 234),
            Text = "White pieces at bottom",
            Checked = true
        };
        Controls.Add(whiteAtBottomCheckBox);

        boardOnlyCheckBox = new MaterialCheckbox
        {
            AutoSize = true,
            Location = new Point(220, 258),
            Text = "Board only (ignore OCR)"
        };
        boardOnlyCheckBox.CheckedChanged += (_, _) =>
        {
            moveRegionLabel.Enabled = !boardOnlyCheckBox.Checked;
            moveRegionButton.Enabled = !boardOnlyCheckBox.Checked;
            UpdateOkState();
        };
        Controls.Add(boardOnlyCheckBox);

        okButton = new MaterialButton
        {
            Text = "Start Tracking",
            Location = new Point(310, 296),
            AutoSize = false,
            Size = new Size(144, 36),
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
