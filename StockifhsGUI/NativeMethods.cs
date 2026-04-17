using System;
using System.Runtime.InteropServices;
using System.Text;

namespace StockifhsGUI;

internal static class NativeMethods
{
    public static WindowCaptureInfo? TryGetForegroundWindowInfo()
    {
        nint handle = GetForegroundWindow();
        if (handle == 0)
        {
            return null;
        }

        StringBuilder titleBuilder = new(512);
        _ = GetWindowText(handle, titleBuilder, titleBuilder.Capacity);
        string title = titleBuilder.ToString().Trim();
        return string.IsNullOrWhiteSpace(title) ? null : new WindowCaptureInfo(handle, title);
    }

    public static bool WindowExists(nint handle) => handle != 0 && IsWindow(handle);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetWindowText(nint hWnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint hWnd);
}
