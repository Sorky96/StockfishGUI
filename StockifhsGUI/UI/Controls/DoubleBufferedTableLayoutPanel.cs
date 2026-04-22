using System.Windows.Forms;

namespace StockifhsGUI;

internal sealed class DoubleBufferedTableLayoutPanel : TableLayoutPanel
{
    public DoubleBufferedTableLayoutPanel()
    {
        DoubleBuffered = true;
        ResizeRedraw = false;
        SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
        UpdateStyles();
    }
}
