using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace P1Z.Subtitler.Native;

internal static class NativeMethods
{
    public const int WmHotkey = 0x0312;
    public const uint ModNoRepeat = 0x4000;
    public const int GwlExStyle = -20;
    public const long WsExTransparent = 0x00000020L;
    public const long WsExToolWindow = 0x00000080L;
    public const long WsExNoActivate = 0x08000000L;
    public const uint SwpNoSize = 0x0001;
    public const uint SwpNoActivate = 0x0010;
    public const uint SwpShowWindow = 0x0040;
    public const uint MonitorDefaultToNearest = 2;
    public static readonly IntPtr HwndTopmost = new(-1);
    public static readonly IntPtr HwndMessage = new(-3);

    public static MonitorArea GetActiveMonitor()
    {
        var foreground = GetForegroundWindow();
        var monitor = foreground != IntPtr.Zero
            ? MonitorFromWindow(foreground, MonitorDefaultToNearest)
            : IntPtr.Zero;
        if (monitor == IntPtr.Zero && GetCursorPos(out var cursor))
            monitor = MonitorFromPoint(cursor, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
            throw new InvalidOperationException("Windows did not provide an active monitor.");
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfoW(monitor, ref info))
            throw new InvalidOperationException("Windows did not provide the monitor work area.");
        return new MonitorArea(monitor, info.Monitor, info.Work, foreground);
    }

    public static void MakeTypingOverlay(IntPtr window)
    {
        var style = GetWindowLongPtr(window, GwlExStyle).ToInt64();
        style |= WsExToolWindow;
        style &= ~(WsExTransparent | WsExNoActivate);
        SetWindowLongPtr(window, GwlExStyle, new IntPtr(style));
    }

    /// <summary>
    /// Moves the overlay without touching its size. WPF owns the size through
    /// SizeToContent; resizing the HWND from here as well races the layout pass
    /// and leaves the newest glyph painted into a stale surface.
    /// </summary>
    public static void MoveTypingOverlay(IntPtr window, int left, int top)
    {
        if (!SetWindowPos(window, HwndTopmost, left, top, 0, 0, SwpNoSize | SwpNoActivate | SwpShowWindow))
            throw new InvalidOperationException("Windows could not position the live subtitle overlay.");
    }

    public static bool TryGetWindowBounds(IntPtr window, out PixelRect bounds) =>
        GetWindowRect(window, out bounds);

    public static double GetMonitorDpiScale(IntPtr monitor)
    {
        return GetDpiForMonitor(monitor, 0, out var dpiX, out _) == 0 && dpiX > 0
            ? dpiX / 96d
            : 1d;
    }

    public static void RestoreForeground(IntPtr window)
    {
        if (window != IntPtr.Zero && IsWindow(window))
            SetForegroundWindow(window);
    }

    public static IntPtr EnsureHandle(System.Windows.Window window) => new WindowInteropHelper(window).EnsureHandle();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(IntPtr window, int id);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfoW(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out PixelRect bounds);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr window, int index, IntPtr value);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr window, int index, int value);

    private static IntPtr GetWindowLongPtr(IntPtr window, int index) =>
        IntPtr.Size == 8 ? GetWindowLongPtr64(window, index) : new IntPtr(GetWindowLong32(window, index));

    private static void SetWindowLongPtr(IntPtr window, int index, IntPtr value)
    {
        if (IntPtr.Size == 8)
            SetWindowLongPtr64(window, index, value);
        else
            SetWindowLong32(window, index, value.ToInt32());
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        public int Size;
        public PixelRect Monitor;
        public PixelRect Work;
        public uint Flags;
    }
}

[StructLayout(LayoutKind.Sequential)]
public struct PixelRect
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
    public readonly int Width => Right - Left;
    public readonly int Height => Bottom - Top;

    public PixelRect(int left, int top, int right, int bottom)
    {
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
    }

    public readonly PixelRect WithPosition(int left, int top) => new(left, top, left + Width, top + Height);
}

public readonly record struct PixelPoint(int X, int Y);

public readonly record struct MonitorArea(IntPtr Handle, PixelRect MonitorBounds, PixelRect WorkArea, IntPtr PreviousForeground);
