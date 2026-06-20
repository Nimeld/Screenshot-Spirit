using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;

namespace ScreenshotSpirit.Services;

public static class CaptureService
{
    /// <summary>Virtual screen bounds in PHYSICAL pixels (across all monitors)</summary>
    public static System.Windows.Int32Rect VirtualScreenBounds
    {
        get
        {
            int left = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
            int top = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
            int width = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
            int height = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);
            return new System.Windows.Int32Rect(left, top, width, height);
        }
    }

    /// <summary>Capture the entire virtual desktop (all monitors)</summary>
    public static BitmapSource CaptureFullVirtualScreen()
    {
        var bounds = VirtualScreenBounds;
        return CaptureRegion(bounds.X, bounds.Y, bounds.Width, bounds.Height);
    }

    public static BitmapSource CaptureRegion(int x, int y, int width, int height)
    {
        var hdcSrc = NativeMethods.CreateDC("DISPLAY", null, null, IntPtr.Zero);
        var hdcDest = NativeMethods.CreateCompatibleDC(hdcSrc);
        var hBitmap = NativeMethods.CreateCompatibleBitmap(hdcSrc, width, height);
        var hOld = NativeMethods.SelectObject(hdcDest, hBitmap);

        NativeMethods.BitBlt(hdcDest, 0, 0, width, height, hdcSrc, x, y, NativeMethods.SRCCOPY);

        NativeMethods.SelectObject(hdcDest, hOld);
        NativeMethods.DeleteDC(hdcDest);
        NativeMethods.DeleteDC(hdcSrc);

        var bs = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
            hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());

        // RenderTargetBitmap: 通过WPF渲染管线创建独立像素副本，彻底脱离HBitmap
        var dv = new System.Windows.Media.DrawingVisual();
        using (var dc = dv.RenderOpen())
            dc.DrawImage(bs, new System.Windows.Rect(0, 0, bs.PixelWidth, bs.PixelHeight));
        var rtb = new RenderTargetBitmap(bs.PixelWidth, bs.PixelHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        rtb.Render(dv);
        NativeMethods.DeleteObject(hBitmap);
        return rtb;
    }

    public static List<WindowInfo> EnumWindows()
    {
        var windows = new List<WindowInfo>();
        NativeMethods.EnumWindows((hWnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hWnd)) return true;
            NativeMethods.GetWindowRect(hWnd, out var rect);
            var sb = new System.Text.StringBuilder(256);
            NativeMethods.GetWindowText(hWnd, sb, sb.Capacity);
            var title = sb.ToString();
            if (string.IsNullOrWhiteSpace(title)) return true;

            windows.Add(new WindowInfo
            {
                Handle = hWnd,
                Title = title,
                Rect = new System.Windows.Int32Rect(rect.Left, rect.Top,
                    rect.Right - rect.Left, rect.Bottom - rect.Top)
            });
            return true;
        }, IntPtr.Zero);
        return windows;
    }

    public static WindowInfo? FindWindowAtPoint(System.Windows.Point screenPoint)
    {
        var hWnd = NativeMethods.WindowFromPoint(screenPoint);
        if (hWnd == IntPtr.Zero) return null;

        NativeMethods.GetWindowRect(hWnd, out var rect);
        var sb = new System.Text.StringBuilder(256);
        NativeMethods.GetWindowText(hWnd, sb, sb.Capacity);
        return new WindowInfo
        {
            Handle = hWnd,
            Title = sb.ToString(),
            Rect = new System.Windows.Int32Rect(rect.Left, rect.Top,
                rect.Right - rect.Left, rect.Bottom - rect.Top)
        };
    }

    /// <summary>Find visible window at a physical screen point, excluding our own process windows.</summary>
    public static WindowInfo? FindVisibleWindowAtPoint(System.Windows.Point screenPoint)
    {
        int px = (int)screenPoint.X;
        int py = (int)screenPoint.Y;
        uint ourPid = NativeMethods.GetWindowThreadProcessId(NativeMethods.GetForegroundWindow(), out _);
        WindowInfo? best = null;
        int bestArea = int.MaxValue;

        NativeMethods.EnumWindows((hWnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hWnd)) return true;

            // Skip our own windows
            NativeMethods.GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == System.Diagnostics.Process.GetCurrentProcess().Id) return true;

            NativeMethods.GetWindowRect(hWnd, out var wRect);
            var sb = new System.Text.StringBuilder(256);
            NativeMethods.GetWindowText(hWnd, sb, sb.Capacity);
            var title = sb.ToString();
            if (string.IsNullOrWhiteSpace(title)) return true;

            var wBounds = GetWindowVisibleBounds(hWnd) ?? new System.Windows.Int32Rect(wRect.Left, wRect.Top, wRect.Right - wRect.Left, wRect.Bottom - wRect.Top);
            int left = wBounds.X, top = wBounds.Y;
            int right = wBounds.X + wBounds.Width, bottom = wBounds.Y + wBounds.Height;
            int w = right - left, h = bottom - top;
            if (w <= 0 || h <= 0) return true;

            // Check if point is within or near this window (within 15px snap distance)
            int snapDist = 15;
            if (px >= left - snapDist && px <= right + snapDist &&
                py >= top - snapDist && py <= bottom + snapDist)
            {
                // Calculate distance from point to window bounds
                int dx = Math.Max(0, Math.Max(left - px, px - right));
                int dy = Math.Max(0, Math.Max(top - py, py - bottom));
                int area = w * h;

                // Prefer smaller windows (more likely to be the target)
                if (best == null || area < bestArea)
                {
                    best = new WindowInfo
                    {
                        Handle = hWnd,
                        Title = title,
                        Rect = new System.Windows.Int32Rect(left, top, w, h)
                    };
                    bestArea = area;
                }
            }
            return true;
        }, IntPtr.Zero);

        return best;
    }
    public class WindowInfo
    {
        public IntPtr Handle { get; set; }
        public string Title { get; set; } = "";
        public System.Windows.Int32Rect Rect { get; set; }
    }
    /// <summary>Get visible bounds of a window excluding invisible resize border (DWM extended frame bounds)</summary>
    public static System.Windows.Int32Rect? GetWindowVisibleBounds(IntPtr hWnd)
    {
        NativeMethods.GetWindowRect(hWnd, out NativeMethods.RECT winRect);
        if (NativeMethods.DwmGetWindowAttribute(hWnd, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS,
            out NativeMethods.RECT extRect,
            System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.RECT>()) == 0)
        {
            return new System.Windows.Int32Rect(extRect.Left, extRect.Top,
                extRect.Right - extRect.Left, extRect.Bottom - extRect.Top);
        }
        return new System.Windows.Int32Rect(winRect.Left, winRect.Top,
            winRect.Right - winRect.Left, winRect.Bottom - winRect.Top);
    }
}




