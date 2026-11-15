using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace EmuLauncher.Services;

internal static class Native
{
    private const int DwmUseImmersiveDarkMode = 20;
    private const int SwRestore = 9;

    /// <summary>A dark title bar for windowed mode (Windows 10 2004 and later; ignored elsewhere).</summary>
    public static void UseDarkTitleBar(Window window)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;

            var enabled = 1;
            DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref enabled, sizeof(int));
        }
        catch
        {
            // Purely cosmetic.
        }
    }

    /// <summary>Brings an emulator that is already running back to the front.</summary>
    public static void BringToFront(Process process)
    {
        try
        {
            process.Refresh();
            var handle = process.MainWindowHandle;
            if (handle == IntPtr.Zero) return;

            if (IsIconic(handle)) ShowWindow(handle, SwRestore);
            SetForegroundWindow(handle);
        }
        catch
        {
            // The process may have just exited.
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr window);
}
