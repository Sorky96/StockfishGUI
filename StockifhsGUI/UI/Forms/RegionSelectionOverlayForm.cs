using System;
using System.Drawing;
using System.Windows.Forms;

namespace StockifhsGUI;

public sealed class RegionSelectionOverlayForm : Form
{
    private Point selectionStart;
    private Point selectionEnd;
    private bool selecting;

    public RegionSelectionOverlayForm(string prompt)
    {
        Bounds = SystemInformation.VirtualScreen;
        BackColor = Color.Black;
        Opacity = 0.25;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        DoubleBuffered = true;
        Cursor = Cursors.Cross;
        KeyPreview = true;

        Label promptLabel = new()
        {
            AutoSize = true,
            BackColor = Color.FromArgb(220, 30, 30, 30),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 11, FontStyle.Bold),
            Padding = new Padding(10),
            Text = prompt,
            Location = new Point(20, 20)
        };

        Controls.Add(promptLabel);

        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
        };

        MouseDown += (_, e) =>
        {
            selecting = true;
            selectionStart = e.Location;
            selectionEnd = e.Location;
            Invalidate();
        };

        MouseMove += (_, e) =>
        {
            if (!selecting)
            {
                return;
            }

            selectionEnd = e.Location;
            Invalidate();
        };

        MouseUp += (_, e) =>
        {
            if (!selecting)
            {
                return;
            }

            selecting = false;
            selectionEnd = e.Location;

            Rectangle localSelection = GetSelectionRectangle();
            if (localSelection.Width < 24 || localSelection.Height < 24)
            {
                Invalidate();
                return;
            }

            SelectedRegion = new Rectangle(
                Left + localSelection.Left,
                Top + localSelection.Top,
                localSelection.Width,
                localSelection.Height);
            DialogResult = DialogResult.OK;
            Close();
        };
    }

    public Rectangle SelectedRegion { get; private set; }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        Rectangle rect = GetSelectionRectangle();
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        using Pen pen = new(Color.Gold, 3);
        using Brush brush = new SolidBrush(Color.FromArgb(70, Color.Gold));
        e.Graphics.FillRectangle(brush, rect);
        e.Graphics.DrawRectangle(pen, rect);
    }

    private Rectangle GetSelectionRectangle()
    {
        int left = Math.Min(selectionStart.X, selectionEnd.X);
        int top = Math.Min(selectionStart.Y, selectionEnd.Y);
        int right = Math.Max(selectionStart.X, selectionEnd.X);
        int bottom = Math.Max(selectionStart.Y, selectionEnd.Y);
        return Rectangle.FromLTRB(left, top, right, bottom);
    }
}
