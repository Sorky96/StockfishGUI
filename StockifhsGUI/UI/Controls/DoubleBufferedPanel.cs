using System.Windows.Forms;

namespace StockifhsGUI;

internal class DoubleBufferedPanel : Panel
{
    public DoubleBufferedPanel()
    {
        DoubleBuffered = true;
        ResizeRedraw = false;
        SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
        UpdateStyles();
    }
}
