using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ScreenshotSpirit.Services;

public class HotkeyService : IDisposable
{
    private readonly Window _window;
    private readonly int _id;
    private bool _registered;

    public event Action? HotkeyPressed;

    public HotkeyService(Window window, uint modifiers, uint key, int id = NativeMethods.HOTKEY_ID)
    {
        _window = window;
        _id = id;
        var handle = new WindowInteropHelper(window).Handle;
        var source = HwndSource.FromHwnd(handle);
        source?.AddHook(WndProc);
        _registered = NativeMethods.RegisterHotKey(handle, id, modifiers, key);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == _id)
        {
            HotkeyPressed?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_registered)
            NativeMethods.UnregisterHotKey(new WindowInteropHelper(_window).Handle, _id);
    }
}
