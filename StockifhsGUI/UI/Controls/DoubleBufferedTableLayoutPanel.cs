using System.Windows.Forms;

namespace StockifhsGUI;

internal sealed class DoubleBufferedTableLayoutPanel : TableLayoutPanel
{
    public DoubleBufferedTableLayoutPanel()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        UpdateStyles();
    }
}
