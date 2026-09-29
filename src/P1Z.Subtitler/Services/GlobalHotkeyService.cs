using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using P1Z.Subtitler.Models;
using P1Z.Subtitler.Native;

namespace P1Z.Subtitler.Services;

internal sealed class GlobalHotkeyService : IDisposable
{
    private const int HotkeyId = 0x51A7;
    private readonly HwndSource _source;
    private bool _registered;

    public GlobalHotkeyService()
    {
        var parameters = new HwndSourceParameters("p1z.subtitler.hotkey")
        {
            ParentWindow = NativeMethods.HwndMessage,
            WindowStyle = 0,
            ExtendedWindowStyle = 0,
            Width = 0,
            Height = 0
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WindowProcedure);
    }

    public event EventHandler? Pressed;

    public void Register(AppSettings settings)
    {
        Unregister();
        var virtualKey = (uint)KeyInterop.VirtualKeyFromKey(settings.HotkeyKey);
        var modifiers = (uint)settings.HotkeyModifiers | NativeMethods.ModNoRepeat;
        if (!NativeMethods.RegisterHotKey(_source.Handle, HotkeyId, modifiers, virtualKey))
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"{Format(settings)} is already in use.");
        _registered = true;
    }

    public void Unregister()
    {
        if (!_registered)
            return;
        NativeMethods.UnregisterHotKey(_source.Handle, HotkeyId);
        _registered = false;
    }

    public static string Format(AppSettings settings)
    {
        var parts = new List<string>();
        if (settings.HotkeyModifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (settings.HotkeyModifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (settings.HotkeyModifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (settings.HotkeyModifiers.HasFlag(HotkeyModifiers.Windows)) parts.Add("Win");
        parts.Add(settings.HotkeyKey.ToString());
        return string.Join(" + ", parts);
    }

    private IntPtr WindowProcedure(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == NativeMethods.WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            Pressed?.Invoke(this, EventArgs.Empty);
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();
        _source.Dispose();
    }
}
