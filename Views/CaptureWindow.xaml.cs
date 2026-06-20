using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Shapes;
using ScreenshotSpirit.Services;

namespace ScreenshotSpirit.Views;

public partial class CaptureWindow : Window
{
    private readonly BitmapSource _fullScreenshot;
    private readonly Int32Rect _virtualBounds;
    private readonly double _dpiScaleX, _dpiScaleY;

    private bool _isDragging;
    private Point _dragStart;
    private Rect _selectionRect;

    private enum ResizeEdge { None, Top, Bottom, Left, Right, TopLeft, TopRight, BottomLeft, BottomRight }
    private ResizeEdge _resizingEdge = ResizeEdge.None;
    private Point _resizeStartPoint;
    private Rect _resizeStartRect;
    private bool _isResizing;

    private bool _isMoving;
    private Point _moveStartPoint;
    private Rect _moveStartRect;

    // Snap state
    private Rect? _snappedWindowRect;
    private bool _isSnapped;
    private bool _snapToFullScreen;
        private DateTime _lastClickTime = DateTime.MinValue;
    private DateTime _lastSnapCheck = DateTime.MinValue;

    private readonly SettingsService _settingsService = new();
    private Models.AppSettings _settings;

    public CaptureWindow(BitmapSource fullScreenshot)
    {
        InitializeComponent();
        _fullScreenshot = fullScreenshot;
        _settings = _settingsService.Load();

        _virtualBounds = CaptureService.VirtualScreenBounds;
        var dpiGroup = VisualTreeHelper.GetDpi(this);
        _dpiScaleX = dpiGroup.DpiScaleX;
        _dpiScaleY = dpiGroup.DpiScaleY;

        Left = _virtualBounds.X / _dpiScaleX;
        Top = _virtualBounds.Y / _dpiScaleY;
        Width = _virtualBounds.Width / _dpiScaleX;
        Height = _virtualBounds.Height / _dpiScaleY;

        ScreenshotImage.Source = fullScreenshot;
        ScreenshotImage.Width = Width;
        ScreenshotImage.Height = Height;

        Loaded += CaptureWindow_Loaded;
    }

    private void CaptureWindow_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateOverlay();
        RootCanvas.MouseDown += RootCanvas_MouseDown;
        RootCanvas.MouseMove += RootCanvas_MouseMove;
        RootCanvas.MouseUp += RootCanvas_MouseUp;
        RootCanvas.KeyDown += RootCanvas_KeyDown;

        BtnConfirm.Click += (_, _) => OpenEditor();
        BtnCancelCapture.Click += (_, _) => { DialogResult = false; Close(); };
        RootCanvas.Focusable = true;
        RootCanvas.Focus();
    }

    // ========== Overlay with selection hole ==========

    private void UpdateOverlay()
    {
        var fullGeo = new RectangleGeometry(new Rect(0, 0, Width, Height));
        if (_selectionRect.Width > 0 && _selectionRect.Height > 0)
        {
            var holeGeo = new RectangleGeometry(_selectionRect);
            OverlayPath.Data = new CombinedGeometry(GeometryCombineMode.Exclude, fullGeo, holeGeo);
        }
        else
        {
            OverlayPath.Data = fullGeo;
        }
        OverlayPath.Visibility = Visibility.Visible;
    }

    // ========== Mouse handling ==========

    private void RootCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        var pos = e.GetPosition(RootCanvas);

        // If snapped to full screen, select everything
        // 吸附确认：点击无拖拽时由 MouseUp 处理
        if (_snapToFullScreen)
        {
            // 不在此打开编辑器，由 MouseUp 处理
        }
        _resizingEdge = GetHandleAtPoint(pos);
        if (_resizingEdge != ResizeEdge.None)
        {
            _resizeStartPoint = pos;
            _resizeStartRect = _selectionRect;
            _isResizing = true;
            return;
        }

        if (_selectionRect.Width > 10 && _selectionRect.Height > 10 && _selectionRect.Contains(pos))
        {
            _isMoving = true;
            _moveStartPoint = pos;
            _moveStartRect = _selectionRect;
            return;
        }

        // Snapped to a window → confirm selection
        // 吸附确认：点击无拖拽时由 MouseUp 处理
        if (_isSnapped && _snappedWindowRect.HasValue)
        {
            // 不在此打开编辑器，由 MouseUp 处理
        }
        // Double-click detection (two clicks within 400ms)
        if (_lastClickTime != DateTime.MinValue && (DateTime.Now - _lastClickTime).TotalMilliseconds < 400)
        {
            _selectionRect = new Rect(0, 0, Width, Height);
            OpenEditor();
            return;
        }
        _lastClickTime = DateTime.Now;

                // Start new selection drag
        _isDragging = true;
        _dragStart = pos;
        _selectionRect = new Rect(pos.X, pos.Y, 0, 0);
        // MouseDown: 不修改视觉树，防止分层窗口捕获丢失
    }


    private ResizeEdge GetHandleAtPoint(Point pos)
    {
        double handleSize = 28;
        double cornerSize = 28;
        double hw = _selectionRect.Width, hh = _selectionRect.Height;
        double rx = _selectionRect.X, ry = _selectionRect.Y;
        
        // Corner detection first
        if (Math.Abs(pos.X - rx) < cornerSize && Math.Abs(pos.Y - ry) < cornerSize) return ResizeEdge.TopLeft;
        if (Math.Abs(pos.X - (rx + hw)) < cornerSize && Math.Abs(pos.Y - ry) < cornerSize) return ResizeEdge.TopRight;
        if (Math.Abs(pos.X - rx) < cornerSize && Math.Abs(pos.Y - (ry + hh)) < cornerSize) return ResizeEdge.BottomLeft;
        if (Math.Abs(pos.X - (rx + hw)) < cornerSize && Math.Abs(pos.Y - (ry + hh)) < cornerSize) return ResizeEdge.BottomRight;
        
        // Edge detection — full edge length (corners already handled above)
        if (Math.Abs(pos.Y - ry) < handleSize && pos.X >= rx && pos.X <= rx + hw) return ResizeEdge.Top;
        if (Math.Abs(pos.Y - (ry + hh)) < handleSize && pos.X >= rx && pos.X <= rx + hw) return ResizeEdge.Bottom;
        if (Math.Abs(pos.X - rx) < handleSize && pos.Y >= ry && pos.Y <= ry + hh) return ResizeEdge.Left;
        if (Math.Abs(pos.X - (rx + hw)) < handleSize && pos.Y >= ry && pos.Y <= ry + hh) return ResizeEdge.Right;
        
        return ResizeEdge.None;
    }
private void RootCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(RootCanvas);

        if (_isDragging)
        {
            double x = Math.Min(_dragStart.X, pos.X), y = Math.Min(_dragStart.Y, pos.Y);
            _selectionRect = new Rect(x, y, Math.Abs(pos.X - _dragStart.X), Math.Abs(pos.Y - _dragStart.Y));
            UpdateSelectionVisual();
            return;
        }

        if (_isResizing)
        {
            double dx = pos.X - _resizeStartPoint.X, dy = pos.Y - _resizeStartPoint.Y;
            var r = _resizeStartRect;
            switch (_resizingEdge)
            {
                case ResizeEdge.Top:         { double b = r.Bottom; r.Y = Math.Min(b - 20, r.Y + dy); r.Height = b - r.Y; break; }
                case ResizeEdge.Bottom:      r.Height = Math.Max(20, r.Height + dy); break;
                case ResizeEdge.Left:        { double rt = r.Right; r.X = Math.Min(rt - 20, r.X + dx); r.Width = rt - r.X; break; }
                case ResizeEdge.Right:       r.Width = Math.Max(20, r.Width + dx); break;
                case ResizeEdge.TopLeft:     { double tr = r.Right; double tb = r.Bottom; r.X = Math.Min(tr - 20, r.X + dx); r.Width = tr - r.X; r.Y = Math.Min(tb - 20, r.Y + dy); r.Height = tb - r.Y; break; }
                case ResizeEdge.TopRight:    { double trb = r.Bottom; r.Width = Math.Max(20, r.Width + dx); r.Y = Math.Min(trb - 20, r.Y + dy); r.Height = trb - r.Y; break; }
                case ResizeEdge.BottomLeft:  { double blr = r.Right; r.X = Math.Min(blr - 20, r.X + dx); r.Width = blr - r.X; r.Height = Math.Max(20, r.Height + dy); break; }
                case ResizeEdge.BottomRight: r.Width = Math.Max(20, r.Width + dx); r.Height = Math.Max(20, r.Height + dy); break;
            }
            _selectionRect = r;
            UpdateSelectionVisual();
            return;
        }

        if (_isMoving)
        {
            _selectionRect = new Rect(
                _moveStartRect.X + pos.X - _moveStartPoint.X,
                _moveStartRect.Y + pos.Y - _moveStartPoint.Y,
                _moveStartRect.Width, _moveStartRect.Height);
            UpdateSelectionVisual();
            return;
        }

        // Cursor feedback + snap detection
        if (_selectionRect.Width > 0 && _selectionRect.Height > 0)
        {
            var edge = GetHandleAtPoint(pos);
            RootCanvas.Cursor = edge != ResizeEdge.None ? GetCursorForEdge(edge) :
                (_selectionRect.Contains(pos) ? Cursors.SizeAll : Cursors.Cross);
        }

        // Always check for snap (window + full-screen)
                // 节流：至少间隔 150ms 才跑一次窗口吸附检测
        var now = DateTime.Now;
        if ((now - _lastSnapCheck).TotalMilliseconds > 150)
        {
            _lastSnapCheck = now;
            CheckSnap(pos);
        }
    }

    private static Cursor GetCursorForEdge(ResizeEdge e) => e switch
    {
        ResizeEdge.Top or ResizeEdge.Bottom => Cursors.SizeNS,
        ResizeEdge.Left or ResizeEdge.Right => Cursors.SizeWE,
        ResizeEdge.TopLeft or ResizeEdge.BottomRight => Cursors.SizeNWSE,
        ResizeEdge.TopRight or ResizeEdge.BottomLeft => Cursors.SizeNESW,
        _ => Cursors.Cross
    };

    // ========== Snap detection ==========

    private void CheckSnap(Point pos)
    {
        // Full-screen snap: near virtual screen edges
        double edgeSnap = 10;
        bool nearLeft = pos.X < edgeSnap;
        bool nearRight = pos.X > Width - edgeSnap;
        bool nearTop = pos.Y < edgeSnap;
        bool nearBottom = pos.Y > Height - edgeSnap;

        if (nearLeft || nearRight || nearTop || nearBottom)
        {
            SnapToFullScreen();
            return;
        }

        // Window snap: near a visible window
        var physPos = new System.Windows.Point(
            pos.X * _dpiScaleX + _virtualBounds.X,
            pos.Y * _dpiScaleY + _virtualBounds.Y);

        var winInfo = CaptureService.FindVisibleWindowAtPoint(physPos);
        if (winInfo != null && winInfo.Rect.Width > 0 && winInfo.Rect.Height > 0)
        {
            double wx = (winInfo.Rect.X - _virtualBounds.X) / _dpiScaleX;
            double wy = (winInfo.Rect.Y - _virtualBounds.Y) / _dpiScaleY;
            double ww = winInfo.Rect.Width / _dpiScaleX;
            double wh = winInfo.Rect.Height / _dpiScaleY;
            var winRect = new Rect(wx, wy, ww, wh);

            double snapDist = 10;
            if (winRect.Contains(pos) ||
                Math.Abs(pos.X - winRect.Left) < snapDist ||
                Math.Abs(pos.X - winRect.Right) < snapDist ||
                Math.Abs(pos.Y - winRect.Top) < snapDist ||
                Math.Abs(pos.Y - winRect.Bottom) < snapDist)
            {
                SnapToWindow(winRect);
                return;
            }
        }

        Unsnap();
    }

    private void SnapToFullScreen()
    {
        if (_snapToFullScreen) return;
        _snapToFullScreen = true;
        _isSnapped = false;
        _snappedWindowRect = null;

        SnapHighlight.Visibility = Visibility.Visible;
        Canvas.SetLeft(SnapHighlight, 0);
        Canvas.SetTop(SnapHighlight, 0);
        SnapHighlight.Width = Width;
        SnapHighlight.Height = Height;
        HideHandles();
    }

    private void SnapToWindow(Rect winRect)
    {
        if (_isSnapped && _snappedWindowRect == winRect) return;
        _snapToFullScreen = false;
        _isSnapped = true;
        _snappedWindowRect = winRect;

        SnapHighlight.Visibility = Visibility.Visible;
        Canvas.SetLeft(SnapHighlight, winRect.X);
        Canvas.SetTop(SnapHighlight, winRect.Y);
        SnapHighlight.Width = winRect.Width;
        SnapHighlight.Height = winRect.Height;
        HideHandles();
    }

    private void Unsnap()
    {
        if (!_isSnapped && !_snapToFullScreen) return;
        _isSnapped = false;
        _snapToFullScreen = false;
        _snappedWindowRect = null;
        SnapHighlight.Visibility = Visibility.Collapsed;
    }

    private void RootCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;

        if (_isDragging)
        {
            _isDragging = false;
            if (_selectionRect.Width > 5 && _selectionRect.Height > 5)
                ShowConfirmPanel();            else
            {
                // 点击无拖拽 → 检查吸附确认
                if (_snapToFullScreen)
                {
                    _selectionRect = new Rect(0, 0, Width, Height);
                    UpdateSelectionVisual();
                    ShowConfirmPanel();
                    return;
                }
                if (_isSnapped && _snappedWindowRect.HasValue)
                {
                    _selectionRect = _snappedWindowRect.Value;
                    UpdateSelectionVisual();
                    ShowConfirmPanel();
                    return;
                }
                _selectionRect = new Rect(0, 0, 0, 0);
                UpdateSelectionVisual();
            }
            return;
        }

        if (_isResizing) { _isResizing = false; return; }
        if (_isMoving) { _isMoving = false; return; }

        if (_isSnapped && _snappedWindowRect.HasValue)
        {
            _selectionRect = _snappedWindowRect.Value;
            OpenEditor();
        }
    }

    // ========== Open EditWindow with all tools ==========

    private void ShowConfirmPanel()
    {
        if (_selectionRect.Width < 5 || _selectionRect.Height < 5) return;
        double px = _selectionRect.X + _selectionRect.Width / 2 - 88;
        double py = _selectionRect.Y + _selectionRect.Height + 8;
        if (py + 40 > Height)
        {
            py = _selectionRect.Y - 44;
            if (py < 0) py = Height - 48;
        }
        Canvas.SetLeft(ConfirmPanel, px);
        Canvas.SetTop(ConfirmPanel, py);
        ConfirmPanel.Visibility = Visibility.Visible;
        ShowHandles();
    }

    private void OpenEditor()
    {
        try
        {
            // 隐藏UI后再截图(避免蓝框/选区线入镜)
            SelectionRect.Visibility = Visibility.Collapsed;
            SnapHighlight.Visibility = Visibility.Collapsed;
            OverlayPath.Visibility = Visibility.Collapsed;

            var region = GetCapturedRegion();
            if (region == null) return;

            var editWin = new EditWindow(region, (int)SystemParameters.PrimaryScreenWidth, (int)SystemParameters.PrimaryScreenHeight);
            editWin.Owner = this;
            editWin.WindowStartupLocation = WindowStartupLocation.CenterScreen;

            if (editWin.ShowDialog() == true)
            {
                DialogResult = true;
            }
            Close();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Edit failed: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ========== Region extraction ==========

    private BitmapSource? GetCapturedRegion()
    {
        if (_selectionRect.Width <= 0 || _selectionRect.Height <= 0) return null;

        int cropX = Math.Max(0, (int)(_selectionRect.X * _dpiScaleX));
        int cropY = Math.Max(0, (int)(_selectionRect.Y * _dpiScaleY));
        int cropW = Math.Max(1, (int)(_selectionRect.Width * _dpiScaleX));
        int cropH = Math.Max(1, (int)(_selectionRect.Height * _dpiScaleY));

        // Extend to bitmap boundary when selection touches window edge
        if (_selectionRect.X < 1) cropX = 0;
        if (_selectionRect.Y < 1) cropY = 0;
        if (_selectionRect.Right > Width - 1) cropW = _fullScreenshot.PixelWidth - cropX;
        if (_selectionRect.Bottom > Height - 1) cropH = _fullScreenshot.PixelHeight - cropY;

        if (cropX + cropW > _fullScreenshot.PixelWidth) cropW = _fullScreenshot.PixelWidth - cropX;
        if (cropY + cropH > _fullScreenshot.PixelHeight) cropH = _fullScreenshot.PixelHeight - cropY;
        if (cropW <= 0 || cropH <= 0) return null;

        return new CroppedBitmap(_fullScreenshot, new Int32Rect(cropX, cropY, cropW, cropH));
    }

    // ========== Visual updates ==========
    private void UpdateConfirmPanelPosition()
    {
        if (ConfirmPanel.Visibility != Visibility.Visible) return;
        double px = _selectionRect.X + _selectionRect.Width / 2 - 88;
        double py = _selectionRect.Y + _selectionRect.Height + 8;
        if (py + 40 > Height)
        {
            py = _selectionRect.Y - 44;
            if (py < 0) py = Height - 48;
        }
        Canvas.SetLeft(ConfirmPanel, px);
        Canvas.SetTop(ConfirmPanel, py);
    }

    private void UpdateSelectionVisual()
    {
        bool v = _selectionRect.Width > 0 && _selectionRect.Height > 0;
        SelectionRect.Visibility = v ? Visibility.Visible : Visibility.Collapsed;
        if (v)
        {
            SelectionRect.Width = _selectionRect.Width;
            SelectionRect.Height = _selectionRect.Height;
            Canvas.SetLeft(SelectionRect, _selectionRect.X);
            Canvas.SetTop(SelectionRect, _selectionRect.Y);
            ShowHandles();
        }
        UpdateOverlay();
    }

    private void ShowHandles()
    {
        if (_selectionRect.Width <= 0 || _selectionRect.Height <= 0) return;
        double hw = 8, hh = 8;
        PlaceHandle(HandleTop, _selectionRect.X + _selectionRect.Width / 2 - hw / 2, _selectionRect.Y - hh / 2);
        PlaceHandle(HandleBottom, _selectionRect.X + _selectionRect.Width / 2 - hw / 2, _selectionRect.Y + _selectionRect.Height - hh / 2);
        PlaceHandle(HandleLeft, _selectionRect.X - hw / 2, _selectionRect.Y + _selectionRect.Height / 2 - hh / 2);
        PlaceHandle(HandleRight, _selectionRect.X + _selectionRect.Width - hw / 2, _selectionRect.Y + _selectionRect.Height / 2 - hh / 2);
        PlaceHandle(HandleTL, _selectionRect.X - hw / 2, _selectionRect.Y - hh / 2);
        PlaceHandle(HandleTR, _selectionRect.X + _selectionRect.Width - hw / 2, _selectionRect.Y - hh / 2);
        PlaceHandle(HandleBL, _selectionRect.X - hw / 2, _selectionRect.Y + _selectionRect.Height - hh / 2);
        PlaceHandle(HandleBR, _selectionRect.X + _selectionRect.Width - hw / 2, _selectionRect.Y + _selectionRect.Height - hh / 2);
    }

    private void PlaceHandle(FrameworkElement h, double x, double y)
    {
        Canvas.SetLeft(h, x); Canvas.SetTop(h, y); h.Visibility = Visibility.Visible;
    }

    private void HideHandles()
    {
        HandleTop.Visibility = Visibility.Collapsed;
        HandleBottom.Visibility = Visibility.Collapsed;
        HandleLeft.Visibility = Visibility.Collapsed;
        HandleRight.Visibility = Visibility.Collapsed;
        HandleTL.Visibility = Visibility.Collapsed;
        HandleTR.Visibility = Visibility.Collapsed;
        HandleBL.Visibility = Visibility.Collapsed;
        HandleBR.Visibility = Visibility.Collapsed;
    }

    private void RootCanvas_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { ConfirmPanel.Visibility = Visibility.Collapsed; DialogResult = false; Close(); }
    }
}










