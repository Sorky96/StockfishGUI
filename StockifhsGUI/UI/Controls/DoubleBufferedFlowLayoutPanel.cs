using System.Windows.Forms;

namespace StockifhsGUI;

internal sealed class DoubleBufferedFlowLayoutPanel : FlowLayoutPanel
{
    public DoubleBufferedFlowLayoutPanel()
    {
        DoubleBuffered = true;
        ResizeRedraw = false;
        SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
        UpdateStyles();
    }
}
