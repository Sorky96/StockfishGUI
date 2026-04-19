using System.Windows.Forms;

namespace StockifhsGUI;

internal sealed class DoubleBufferedFlowLayoutPanel : FlowLayoutPanel
{
    public DoubleBufferedFlowLayoutPanel()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        UpdateStyles();
    }
}
