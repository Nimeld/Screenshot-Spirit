using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Ink;
using ScreenshotSpirit.Models;
using ScreenshotSpirit.Services;

namespace ScreenshotSpirit.Views;

public enum EditTool { Rectangle, Circle, Line, Curve, Arrow, Mosaic, Text }

public partial class EditWindow : Window
{
    private readonly SettingsService _settingsService = new();
    private AppSettings _settings;

    // Source image dimensions
    private readonly int _imageW;
    private readonly int _imageH;

    // Drawing state
    private EditTool _currentTool = EditTool.Rectangle;
    private bool _isDrawing;
    private Point _drawStart;
    private Shape? _previewShape;
    private List<UIElement> _drawnElements = new();
    private readonly Stack<List<UIElement>> _undoStack = new();
    private readonly Stack<List<UIElement>> _redoStack = new();
    private readonly Stack<StrokeCollection> _inkRedoStack = new();

    // Mosaic brush state
    private int _mosaicPixelSize = 10;
    private string _textFontFamily = "Microsoft YaHei";
    private bool _textBold = false;
    private bool _textItalic = false;
    private bool _textUnderline = false;
    private TextBox? _activeTextBox;
    private TextBlock? _selectedText;
    private Rectangle? _brushCursor;
    private bool _isMosaicPainting;
    private readonly HashSet<(int, int)> _mosaicStrokePositions = new();
    private readonly List<Rectangle> _mosaicStrokeRedRects = new();
    private readonly Dictionary<(int, int), UIElement> _mosaicBlockMap = new();
    private RenderTargetBitmap? _mosaicBmp;
    private System.Windows.Controls.Image? _mosaicImg;
    private Point? _lastMosaicPos;
    private double _screenW, _screenH;
    private double _canvasW, _canvasH;
    private bool _isLargeCanvas;
    private double _minZoom = 1.0;
    // Zoom / pan
    private double _currentZoom = 1.0;
    private bool _isSpaceDown = false;
    private bool _isPanning = false;
    private Point _panStart;
    private double _panStartX, _panStartY;
    private readonly ScaleTransform _zoomTransform = new();
    private readonly TranslateTransform _panTransform = new();
    private double _panX, _panY;
    private double _zoom = 1.0;
    private Path? _mosaicPreviewPath;
    private PathFigure? _mosaicPreviewFig;
    private PathGeometry? _mosaicPreviewGeo;

    private Color _currentColor = Colors.Red;
    private double _currentSize = 3;
    private double _lastRegularSize = 3;

    public EditWindow(BitmapSource sourceImage, int screenW, int screenH)
    {
        InitializeComponent();
        _settings = _settingsService.Load();

        _imageW = sourceImage.PixelWidth;
        _imageH = sourceImage.PixelHeight;

        SourceImage.Source = sourceImage;
        SourceImage.Width = _imageW;
        SourceImage.Height = _imageH;
        DrawCanvas.Width = _imageW;
        DrawCanvas.Height = _imageH;
        MosaicCanvas.Width = _imageW;
        MosaicCanvas.Height = _imageH;
        _mosaicImg = new System.Windows.Controls.Image { Stretch = Stretch.None, Width = _imageW, Height = _imageH, HorizontalAlignment = System.Windows.HorizontalAlignment.Left, VerticalAlignment = System.Windows.VerticalAlignment.Top };
        var pp = SourceImage.Parent as System.Windows.Controls.Panel;
        if (pp != null) { pp.Children.Remove(MosaicCanvas); pp.Children.Insert(1, _mosaicImg); }
        InkCanvas.Width = _imageW;
        InkCanvas.Height = _imageH;

        // Calculate canvas mode and dimensions
        _screenW = screenW;
        _screenH = screenH;
        double thresholdW = screenW * 0.85;
        double thresholdH = screenH * 0.85;
        _isLargeCanvas = _imageW >= thresholdW || _imageH >= thresholdH;
        if (_isLargeCanvas)
        {
            double imgAspect = _imageW / (double)_imageH;
            double maxAspect = thresholdW / thresholdH;
            if (imgAspect >= maxAspect)
            {
                _canvasW = thresholdW;
                _canvasH = thresholdW / imgAspect;
            }
            else
            {
                _canvasH = thresholdH;
                _canvasW = thresholdH * imgAspect;
            }
            _minZoom = _canvasW / _imageW;
        }
        else
        {
            _canvasW = _imageW;
            _canvasH = _imageH;
            // Minimum canvas size for UI to fit
            double minCanvasW = 780, minCanvasH = 120;
            if (_canvasW < minCanvasW || _canvasH < minCanvasH)
            {
                double scaleX = minCanvasW / _canvasW;
                double scaleY = minCanvasH / _canvasH;
                double scaleUp = Math.Max(scaleX, scaleY);
                _minZoom = scaleUp;
                _canvasW = _imageW * scaleUp;
                _canvasH = _imageH * scaleUp;
            }
        }

        // Apply canvas and window sizing NOW (before window shows)
        ApplyCanvasLayout();

        InkCanvas.StrokeCollected += InkCanvas_StrokeCollected;

        SizeSlider.Value = _currentSize;
        ColorPreview.Fill = new SolidColorBrush(_currentColor);

        ToolRect.IsChecked = true;
        KeyDown += EditWindow_KeyDown;

        // Scale via LayoutTransform (beats ClipToBounds), pan via RenderTransform
        CanvasGrid.LayoutTransform = _zoomTransform;
        var panGroup = new TransformGroup();
        panGroup.Children.Add(_panTransform);
        CanvasGrid.RenderTransform = panGroup;
        CanvasGrid.RenderTransformOrigin = new Point(0, 0);

        // Zoom/pan events
        PreviewKeyDown += EditWindow_PreviewKeyDown;
        PreviewKeyUp += EditWindow_PreviewKeyUp;
        CanvasContainer.PreviewMouseWheel += CanvasContainer_PreviewMouseWheel;
        CanvasContainer.PreviewMouseDown += CanvasContainer_PreviewMouseDown;
        CanvasContainer.PreviewMouseMove += CanvasContainer_PreviewMouseMove;
        CanvasContainer.PreviewMouseUp += CanvasContainer_PreviewMouseUp;
        PreviewMouseMove += EditWindow_PreviewMouseMove;

        // Canvas mouse events
        DrawCanvas.MouseDown += DrawCanvas_MouseDown;
        DrawCanvas.MouseMove += DrawCanvas_MouseMove;
        DrawCanvas.MouseUp += DrawCanvas_MouseUp;
        MosaicSizeSlider.ValueChanged += MosaicSizeSlider_ValueChanged;
        SizeSlider.ValueChanged += (_, _) =>
        {
            _currentSize = SizeSlider.Value;
            if (_brushCursor != null)
            {
                _brushCursor.Width = _currentSize;
                _brushCursor.Height = _currentSize;
            }
        };

        // Re-apply zoom/pan after window layout completes
        Loaded += (_, _) => { FontFamilyBox.SelectedIndex = 0; ApplyZoomAndPan(); try { var sb = new System.Text.StringBuilder(); sb.AppendLine($"z={_currentZoom:F3} s=({_imageW*_currentZoom:F1}x{_imageH*_currentZoom:F1}) c=({_canvasW:F1}x{_canvasH:F1}) i=({_imageW}x{_imageH})"); sb.AppendLine($"Win Actual=({ActualWidth:F1}x{ActualHeight:F1}) Win Set=({Width:F1}x{Height:F1})"); sb.AppendLine($"CC Actual=({CanvasContainer.ActualWidth:F1}x{CanvasContainer.ActualHeight:F1}) CC Set=({CanvasContainer.Width:F1}x{CanvasContainer.Height:F1})"); sb.AppendLine($"CG Actual=({CanvasGrid.ActualWidth:F1}x{CanvasGrid.ActualHeight:F1}) CG RS=({CanvasGrid.RenderSize.Width:F1}x{CanvasGrid.RenderSize.Height:F1})"); sb.AppendLine($"SI Actual=({SourceImage.ActualWidth:F1}x{SourceImage.ActualHeight:F1}) DC Actual=({DrawCanvas.ActualWidth:F1}x{DrawCanvas.ActualHeight:F1})"); sb.AppendLine($"Scr=({_screenW}x{_screenH}) maxW=({(_screenW-20)}x{(_screenH-20)}) minZ={_minZoom:F4}"); sb.AppendLine($"zT=({_zoomTransform.ScaleX:F3},{_zoomTransform.ScaleY:F3}) pT=({_panTransform.X:F1},{_panTransform.Y:F1}) pXY=({_panX:F1},{_panY:F1})"); System.IO.File.WriteAllText(@"C:\temp\ss_zoom.txt", sb.ToString()); } catch {} };
    }


    private void ApplyZoomAndPan()
    {
        _zoomTransform.ScaleX = _zoom;
        _zoomTransform.ScaleY = _zoom;
        _panTransform.X = _panX;
        _panTransform.Y = _panY;
    }

    private void InkCanvas_StrokeCollected(object? sender, InkCanvasStrokeCollectedEventArgs e)
    {
        var stroke = e.Stroke;
        bool outside = true;
        foreach (var pt in stroke.StylusPoints)
        {
            if (pt.X >= 0 && pt.Y >= 0 && pt.X <= _imageW && pt.Y <= _imageH)
            {
                outside = false;
                break;
            }
        }
        if (outside && InkCanvas.Strokes.Count > 0)
            InkCanvas.Strokes.RemoveAt(InkCanvas.Strokes.Count - 1);
        else
            _inkRedoStack.Clear();
    }

    private void Tool_Checked(object sender, RoutedEventArgs e)
    {
        if (sender == ToolRect) _currentTool = EditTool.Rectangle;
        else if (sender == ToolCircle) _currentTool = EditTool.Circle;
        else if (sender == ToolLine) _currentTool = EditTool.Line;
        else if (sender == ToolCurve) _currentTool = EditTool.Curve;
        else if (sender == ToolArrow) _currentTool = EditTool.Arrow;
        else if (sender == ToolMosaic) _currentTool = EditTool.Mosaic;
        else if (sender == ToolText) _currentTool = EditTool.Text;

        UpdateCanvasMode();
    }

    private void UpdateCanvasMode()
    {
        if (InkCanvas == null) return;

        if (_currentTool == EditTool.Curve)
        {
            InkCanvas.Visibility = Visibility.Visible;
            DrawCanvas.IsHitTestVisible = false;
            InkCanvas.IsHitTestVisible = true;
            InkCanvas.EditingMode = InkCanvasEditingMode.Ink;
            InkCanvas.DefaultDrawingAttributes = new System.Windows.Ink.DrawingAttributes
            {
                Color = _currentColor,
                Width = SizeSlider.Value,
                Height = SizeSlider.Value,
                FitToCurve = true
            };
        }
        else
        {
            // Convert remaining ink strokes to Path elements before hiding
            if (InkCanvas.Strokes.Count > 0)
            {
                FlattenInkStrokes();
            }

            InkCanvas.Visibility = Visibility.Collapsed;
            DrawCanvas.IsHitTestVisible = true;
            InkCanvas.IsHitTestVisible = false;
        }

        bool isMosaic = _currentTool == EditTool.Mosaic;

        // Save/restore size between mosaic and other tools
        bool wasMosaic = _brushCursor != null; // already had mosaic setup before
        if (isMosaic && !wasMosaic)
        {
            // Switching TO mosaic: save current size, set to 35
            _lastRegularSize = SizeSlider.Value;
            SizeSlider.Value = 35;
        }
        else if (!isMosaic && wasMosaic)
        {
            // Switching FROM mosaic: restore previous size
            SizeSlider.Value = _lastRegularSize;
        }

        // Show/hide mosaic controls
        SizeLabel.Visibility = isMosaic ? Visibility.Collapsed : Visibility.Visible;
        BrushSizeLabel.Visibility = isMosaic ? Visibility.Visible : Visibility.Collapsed;
        _currentSize = SizeSlider.Value;
        if (_brushCursor != null)
        {
            _brushCursor.Width = _currentSize;
            _brushCursor.Height = _currentSize;
        }

        // Show/hide color controls (not needed for mosaic)
        bool isText = _currentTool == EditTool.Text;
        FontLabel.Visibility = isText ? Visibility.Visible : Visibility.Collapsed;
        FontFamilyBox.Visibility = isText ? Visibility.Visible : Visibility.Collapsed;
        BtnBold.Visibility = isText ? Visibility.Visible : Visibility.Collapsed;
        BtnItalic.Visibility = isText ? Visibility.Visible : Visibility.Collapsed;
        BtnUnderline.Visibility = isText ? Visibility.Visible : Visibility.Collapsed;
        SizeLabel.Text = isText ? "字号:" : "粗细:";
        if (isText) { double saved = SizeSlider.Value; SizeSlider.Minimum = 8; SizeSlider.Maximum = 72; if (saved < 8) SizeSlider.Value = 24; }
        else { SizeSlider.Minimum = 1; SizeSlider.Maximum = 80; }
        _currentSize = SizeSlider.Value;
        ColorLabel.Visibility = isMosaic ? Visibility.Collapsed : Visibility.Visible;
        ColorPreview.Visibility = isMosaic ? Visibility.Collapsed : Visibility.Visible;
        PickColorBtn.Visibility = isMosaic ? Visibility.Collapsed : Visibility.Visible;

        // Show/hide mosaic grain slider
        MosaicSizeLabel.Visibility = isMosaic ? Visibility.Visible : Visibility.Collapsed;
        MosaicSizeSlider.Visibility = isMosaic ? Visibility.Visible : Visibility.Collapsed;
        MosaicSizeValue.Visibility = isMosaic ? Visibility.Visible : Visibility.Collapsed;

        // Brush cursor and preview path for mosaic
        if (isMosaic && _brushCursor == null)
        {
            _brushCursor = new Rectangle
            {
                Width = _currentSize,
                Height = _currentSize,
                Stroke = Brushes.White,
                StrokeThickness = 2,
                Fill = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                IsHitTestVisible = false
            };
            DrawCanvas.Children.Add(_brushCursor);

            // Smooth brush preview path (replaces grid-aligned red rects)
            _mosaicPreviewGeo = new PathGeometry();
            _mosaicPreviewFig = new PathFigure();
            _mosaicPreviewGeo.Figures.Add(_mosaicPreviewFig);
            _mosaicPreviewPath = new Path
            {
                Data = _mosaicPreviewGeo,
                Stroke = new SolidColorBrush(Color.FromArgb(80, 255, 0, 0)),
                StrokeThickness = _currentSize,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                IsHitTestVisible = false
            };
            DrawCanvas.Children.Add(_mosaicPreviewPath);

            DrawCanvas.Cursor = Cursors.None;
        }
        else if (!isMosaic && _brushCursor != null)
        {
            DrawCanvas.Children.Remove(_brushCursor);
            _brushCursor = null;
            if (_mosaicPreviewPath != null)
            {
                DrawCanvas.Children.Remove(_mosaicPreviewPath);
                _mosaicPreviewPath = null;
                _mosaicPreviewFig = null;
                _mosaicPreviewGeo = null;
            }
            DrawCanvas.Cursor = Cursors.Arrow;
        }
    }

    /// <summary>
    /// Convert InkCanvas strokes to Path elements on DrawCanvas so they stay visible
    /// when switching away from Curve tool.
    /// </summary>
    private void FlattenInkStrokes()
    {
        if (InkCanvas.Strokes.Count == 0) return;
        foreach (var stroke in InkCanvas.Strokes)
        {
            var pts = stroke.StylusPoints;
            if (pts.Count < 2) continue;

            var geo = new PathGeometry();
            var fig = new PathFigure { StartPoint = pts[0].ToPoint() };
            var seg = new PolyLineSegment();
            for (int i = 1; i < pts.Count; i++)
                seg.Points.Add(pts[i].ToPoint());
            fig.Segments.Add(seg);
            geo.Figures.Add(fig);

            var path = new Path
            {
                Data = geo,
                Stroke = new SolidColorBrush(stroke.DrawingAttributes.Color),
                StrokeThickness = stroke.DrawingAttributes.Width,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round
            };
            Canvas.SetLeft(path, 0);
            Canvas.SetTop(path, 0);
            DrawCanvas.Children.Add(path);
            _undoStack.Push(new List<UIElement>(_drawnElements));
            _redoStack.Clear();
            _drawnElements.Add(path);
        }

        InkCanvas.Strokes.Clear();
    }

    private void BtnPickColor_Click(object sender, RoutedEventArgs e)
    {
        var colors = new Color[] {
            Colors.Red, Colors.Orange, Colors.Yellow, Colors.Green, Colors.Blue,
            Colors.Purple, Colors.Cyan, Colors.Black, Colors.Gray, Colors.White
        };

        var picker = new Window
        {
            Title = "Pick Color",
            Width = 290, Height = 80,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            WindowStyle = WindowStyle.ToolWindow,
            ResizeMode = ResizeMode.NoResize,
            Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x2D))
        };

        var wrap = new WrapPanel { Margin = new Thickness(6) };
        foreach (var c in colors)
        {
            var r = new Rectangle
            {
                Width = 26, Height = 26,
                Fill = new SolidColorBrush(c),
                Stroke = Brushes.White,
                StrokeThickness = 1,
                Margin = new Thickness(2),
                Cursor = Cursors.Hand
            };
            r.MouseDown += (_, _) =>
            {
                _currentColor = c;
                ColorPreview.Fill = new SolidColorBrush(c);
                if (_currentTool == EditTool.Curve && InkCanvas != null)
                    InkCanvas.DefaultDrawingAttributes.Color = c;
                picker.Close();
            };
            wrap.Children.Add(r);
        }
        picker.Content = wrap;
        picker.ShowDialog();
    }

    // ========== Drawing on Canvas ==========

    private void DrawCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        var pos = e.GetPosition(DrawCanvas);
        if (pos.X < 0 || pos.Y < 0 || pos.X > _imageW || pos.Y > _imageH) return;
        _drawStart = pos;
        _isDrawing = true;
        _currentSize = SizeSlider.Value;

        if (_currentTool == EditTool.Mosaic)
        {
            _isMosaicPainting = true;
            _mosaicStrokePositions.Clear();
            // Reset preview path for new stroke
            _mosaicPreviewFig = new PathFigure { StartPoint = pos };
            _mosaicPreviewGeo = new PathGeometry();
            _mosaicPreviewGeo.Figures.Add(_mosaicPreviewFig);
            if (_mosaicPreviewPath != null)
            {
                _mosaicPreviewPath.Data = _mosaicPreviewGeo;
            }
            else
            {
                _mosaicPreviewPath = new Path
                {
                    Data = _mosaicPreviewGeo,
                    Stroke = new SolidColorBrush(Color.FromArgb(80, 255, 0, 0)),
                    StrokeThickness = _currentSize,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    StrokeLineJoin = PenLineJoin.Round,
                    IsHitTestVisible = false
                };
                DrawCanvas.Children.Add(_mosaicPreviewPath);
            }
            _lastMosaicPos = null;
            PaintMosaicBlockAt(pos);
            _lastMosaicPos = pos;
            return;
        }

        // Save undo state
        _undoStack.Push(new List<UIElement>(_drawnElements));
        _redoStack.Clear();

        if (_currentTool == EditTool.Text)
        {
            AddTextAt(pos);
            _isDrawing = false;
            return;
        }
    }

    private void DrawCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(DrawCanvas);

        // Mosaic: update cursor even when not painting (before _isDrawing check)
        if (_currentTool == EditTool.Mosaic)
        {
            // Update brush cursor position and keep it on top
            if (_brushCursor != null)
            {
                double half = _currentSize / 2.0;
                Canvas.SetLeft(_brushCursor, pos.X - half);
                Canvas.SetTop(_brushCursor, pos.Y - half);
                // Bring cursor to front so it isn't covered by painted areas
                int maxIdx = DrawCanvas.Children.Count - 1;
                int curIdx = DrawCanvas.Children.IndexOf(_brushCursor);
                if (curIdx >= 0 && curIdx < maxIdx)
                {
                    DrawCanvas.Children.RemoveAt(curIdx);
                    DrawCanvas.Children.Add(_brushCursor);
                }
            }

            if (_isMosaicPainting)
            {
                // Add line segment to smooth preview path
                if (_mosaicPreviewFig != null)
                    _mosaicPreviewFig.Segments.Add(new LineSegment(pos, true));
                if (_mosaicPreviewPath != null)
                    _mosaicPreviewPath.StrokeThickness = _currentSize;

                // Interpolate to fill gaps from fast mouse movement
                if (_lastMosaicPos.HasValue)
                {
                    var prev = _lastMosaicPos.Value;
                    double dist = Math.Sqrt(Math.Pow(pos.X - prev.X, 2) + Math.Pow(pos.Y - prev.Y, 2));
                    if (dist > _mosaicPixelSize)
                    {
                        int steps = (int)(dist / (_mosaicPixelSize * 0.5));
                        for (int s = 0; s <= steps; s++)
                        {
                            double t_s = (double)s / steps;
                            var interp = new Point(
                                prev.X + (pos.X - prev.X) * t_s,
                                prev.Y + (pos.Y - prev.Y) * t_s);
                            PaintMosaicBlockAt(interp);
                        }
                    }
                    else
                    {
                        PaintMosaicBlockAt(pos);
                    }
                }
                else
                {
                    PaintMosaicBlockAt(pos);
                }
                _lastMosaicPos = pos;
            }
            return;
        }

        if (!_isDrawing) return;
        // 更新预览图形（不重复创建/删除，避免频繁操作视觉树）
        if (_previewShape == null)
        {
            // 首次创建预览图形
            var shape = CreateShape(_currentTool, _drawStart, pos);
            if (shape != null)
            {
                if (shape is not Line)
                {
                    double sx = Math.Min(_drawStart.X, pos.X);
                    double sy = Math.Min(_drawStart.Y, pos.Y);
                if (_currentTool != EditTool.Arrow)
                    Canvas.SetLeft(shape, sx);
                if (_currentTool != EditTool.Arrow)
                    Canvas.SetTop(shape, sy);
                }
                DrawCanvas.Children.Add(shape);
                _previewShape = shape;
            }
        }
        else
        {
            // 更新已有预览图形的位置/尺寸
            double nx = Math.Min(_drawStart.X, pos.X);
            double ny = Math.Min(_drawStart.Y, pos.Y);
            double nw = Math.Abs(pos.X - _drawStart.X);
            double nh = Math.Abs(pos.Y - _drawStart.Y);
            if (_previewShape is Line line)
            {
                // Line 不使用 Width/Height 和 Canvas.Left/Top
                line.X2 = pos.X;
                line.Y2 = pos.Y;
            }
            else if (_currentTool == EditTool.Arrow && _previewShape is Path arrowPath)
            {
                var newArrow = CreateArrowShape(_drawStart, pos, new SolidColorBrush(_currentColor), _currentSize);
                if (newArrow is Path newP)
                    arrowPath.Data = newP.Data;
            }
            else
            {
                Canvas.SetLeft(_previewShape, nx);
                Canvas.SetTop(_previewShape, ny);
                if (_previewShape is FrameworkElement fe)
                {
                    fe.Width = nw;
                    fe.Height = nh;
                }
            }
        }
    }

    private Shape? CreateShape(EditTool tool, Point start, Point end)
    {
        var brush = new SolidColorBrush(_currentColor);
        double size = _currentSize;
        double x = Math.Min(start.X, end.X);
        double y = Math.Min(start.Y, end.Y);
        double w = Math.Abs(end.X - start.X);
        double h = Math.Abs(end.Y - start.Y);

        return tool switch
        {
            EditTool.Rectangle => new Rectangle
            {
                Stroke = brush,
                StrokeThickness = size,
                                Fill = Brushes.Transparent,
                Width = w,
                Height = h
            },

            EditTool.Circle => new Ellipse
            {
                Stroke = brush,
                StrokeThickness = size,
                                Fill = Brushes.Transparent,
                Width = w,
                Height = h
            },

            EditTool.Line => new Line
            {
                Stroke = brush,
                StrokeThickness = size,
                X1 = start.X, Y1 = start.Y,
                X2 = end.X, Y2 = end.Y
            },

            EditTool.Arrow => CreateArrowShape(start, end, brush, size),

            _ => null
        };
    }

    private Shape CreateArrowShape(Point start, Point end, Brush brush, double size)
    {
        var geometry = new PathGeometry();

        // Line shaft
        geometry.AddGeometry(new LineGeometry(start, end));

        double angle = Math.Atan2(end.Y - start.Y, end.X - start.X);
        double arrowLen = Math.Max(12, size * 3);

        Point p1 = new Point(
            end.X - arrowLen * Math.Cos(angle - 0.4),
            end.Y - arrowLen * Math.Sin(angle - 0.4));
        Point p2 = new Point(
            end.X - arrowLen * Math.Cos(angle + 0.4),
            end.Y - arrowLen * Math.Sin(angle + 0.4));

        // Arrowhead (same PathGeometry, different figure)
        var headFig = new PathFigure { StartPoint = end };
        headFig.Segments.Add(new LineSegment(p1, true));
        headFig.Segments.Add(new LineSegment(p2, true));
        headFig.IsClosed = true;
        geometry.Figures.Add(headFig);

        var path = new Path
        {
            Data = geometry,
            Stroke = brush,
            Fill = brush,
            StrokeThickness = size,
        };
        Canvas.SetLeft(path, 0);
        Canvas.SetTop(path, 0);
        return path;
    }

    private void DrawCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDrawing) return;
        _isDrawing = false;

        if (_previewShape != null)
        {
            _drawnElements.Add(_previewShape);
        }

        _previewShape = null;

        if (_currentTool == EditTool.Mosaic)
        {
            _isMosaicPainting = false;
            FinalizeMosaicStroke();
            return;
        }
    }



    // ========== Text tool ==========

    private void AddTextAt(Point pos)
    {
        double fontSize = Math.Clamp(_currentSize, 8, 72);
        var colorBrush = new SolidColorBrush(_currentColor);

        var tb = new TextBox
        {
            MinWidth = 60,
            MaxWidth = Math.Max(400, _imageW - pos.X - 10),
            FontSize = fontSize,
            FontFamily = new System.Windows.Media.FontFamily(_textFontFamily),
            FontWeight = _textBold ? FontWeights.Bold : FontWeights.Normal,
            FontStyle = _textItalic ? FontStyles.Italic : FontStyles.Normal,
            Foreground = colorBrush,
            Background = new SolidColorBrush(Color.FromArgb(50, 0, 0, 0)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(160, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            Text = "",
            CaretBrush = colorBrush
        };

        Canvas.SetLeft(tb, pos.X);
        Canvas.SetTop(tb, pos.Y);
        DrawCanvas.Children.Add(tb);
        _drawnElements.Add(tb);

        tb.PreviewKeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape)
            {
                DrawCanvas.Children.Remove(tb);
                _drawnElements.Remove(tb);
                args.Handled = true;
            }
        };

        tb.LostFocus += (_, _) =>
        {
            if (!DrawCanvas.Children.Contains(tb)) return;
            FinalizeTextBox(tb, colorBrush);
        };

        tb.Focus();
    }

    private void FinalizeTextBox(TextBox tb, Brush foreBrush)
    {
        string text = tb.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            DrawCanvas.Children.Remove(tb);
            _drawnElements.Remove(tb);
            return;
        }

        double left = Canvas.GetLeft(tb);
        double top = Canvas.GetTop(tb);

        var block = new TextBlock
        {
            Text = text,
            FontSize = tb.FontSize,
            FontFamily = tb.FontFamily,
            FontWeight = tb.FontWeight,
            FontStyle = tb.FontStyle,
            Foreground = foreBrush,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = tb.MaxWidth,
            Background = Brushes.Transparent,
            Tag = "Text",
            Cursor = Cursors.SizeAll,
            TextDecorations = _textUnderline ? TextDecorations.Underline : null
        };

        Canvas.SetLeft(block, left);
        Canvas.SetTop(block, top);




        bool dragging = false;
        Point dragStart;
        double origLeft = 0, origTop = 0;
        bool resizing = false;
        Point resizeStart;
        double origFontSize = 0;
        block.MouseLeftButtonDown += (_, e) =>
        {
            var pos = e.GetPosition(block);
            bool nearCorner = pos.X > block.ActualWidth - 20 && pos.Y > block.ActualHeight - 20;
            if (e.ClickCount == 2 && !nearCorner)
            {
                DrawCanvas.Children.Remove(block);
                _drawnElements.Remove(block);
                var reEdit = new TextBox
                {
                    Text = text,
                    MinWidth = 60,
                    MaxWidth = Math.Max(400, _imageW - left - 10),
                    FontSize = tb.FontSize,
                    FontFamily = new System.Windows.Media.FontFamily(_textFontFamily),
                    FontWeight = _textBold ? FontWeights.Bold : FontWeights.Normal,
                    FontStyle = _textItalic ? FontStyles.Italic : FontStyles.Normal,
                    Foreground = foreBrush,
                    Background = new SolidColorBrush(Color.FromArgb(50, 0, 0, 0)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(160, 255, 255, 255)),
                    BorderThickness = new Thickness(1),
                    TextWrapping = TextWrapping.Wrap,
                    AcceptsReturn = true,
                    CaretBrush = foreBrush
                };
                Canvas.SetLeft(reEdit, left);
                Canvas.SetTop(reEdit, top);
                DrawCanvas.Children.Add(reEdit);
                _drawnElements.Add(reEdit);
                reEdit.LostFocus += (_, _) =>
                {
                    if (!DrawCanvas.Children.Contains(reEdit)) return;
                    FinalizeTextBox(reEdit, foreBrush);
                };
                reEdit.PreviewKeyDown += (_, args) =>
                {
                    if (args.Key == Key.Escape)
                    {
                        DrawCanvas.Children.Remove(reEdit);
                        _drawnElements.Remove(reEdit);
                        args.Handled = true;
                    }
                };
                reEdit.Focus();
                reEdit.CaretIndex = reEdit.Text.Length;
                e.Handled = true;
                return;
            }
            if (nearCorner)
            {
                resizing = true;
                resizeStart = e.GetPosition(DrawCanvas);
                origFontSize = block.FontSize;
                block.CaptureMouse();
                e.Handled = true;
            }
            else
            {
                dragging = true;
                dragStart = e.GetPosition(DrawCanvas);
                origLeft = Canvas.GetLeft(block);
                origTop = Canvas.GetTop(block);
                block.CaptureMouse();
                e.Handled = true;
                if (_selectedText != null && _selectedText != block)
                    _selectedText.Background = Brushes.Transparent;
                _selectedText = block;
                block.Background = new SolidColorBrush(Color.FromArgb(40, 80, 140, 255));
            }
        };
        block.MouseMove += (_, e) =>
        {
            if (resizing)
            {
                var pos = e.GetPosition(DrawCanvas);
                double delta = Math.Max(pos.X - resizeStart.X, pos.Y - resizeStart.Y);
                block.FontSize = Math.Clamp(origFontSize + delta * 0.6, 8, 200);
                return;
            }
            if (!dragging) return;
            var dpos = e.GetPosition(DrawCanvas);
            double newLeft = origLeft + (dpos.X - dragStart.X);
            double newTop = origTop + (dpos.Y - dragStart.Y);
            newLeft = Math.Clamp(newLeft, 0, _imageW - block.ActualWidth);
            newTop = Math.Clamp(newTop, 0, _imageH - block.ActualHeight);
            Canvas.SetLeft(block, newLeft);
            Canvas.SetTop(block, newTop);
        };
        block.MouseLeftButtonUp += (_, _) =>
        {
            dragging = false;
            resizing = false;
            block.ReleaseMouseCapture();
        };



        if (_activeTextBox == tb) _activeTextBox = null;
        int idx = DrawCanvas.Children.IndexOf(tb);
        int listIdx = _drawnElements.IndexOf(tb);
        DrawCanvas.Children.Remove(tb);
        _drawnElements.Remove(tb);
        if (idx >= 0 && idx <= DrawCanvas.Children.Count)
            DrawCanvas.Children.Insert(idx, block);
        else
            DrawCanvas.Children.Add(block);
        if (listIdx >= 0 && listIdx <= _drawnElements.Count)
            _drawnElements.Insert(listIdx, block);
        else
            _drawnElements.Add(block);
    }
// ========== Undo / Redo ==========

    private void BtnUndo_Click(object sender, RoutedEventArgs e)
    {
        // Curve tool: undo last ink stroke
        if (_currentTool == EditTool.Curve && InkCanvas.Strokes.Count > 0)
        {
            _inkRedoStack.Push(InkCanvas.Strokes.Clone());
            InkCanvas.Strokes.RemoveAt(InkCanvas.Strokes.Count - 1);
            return;
        }

        if (_undoStack.Count == 0) return;
        _redoStack.Push(new List<UIElement>(_drawnElements));
        _drawnElements = _undoStack.Pop();
        RebuildCanvas();
    }

    private void BtnRedo_Click(object sender, RoutedEventArgs e)
    {
        // Curve tool: redo ink stroke
        if (_inkRedoStack.Count > 0)
        {
            InkCanvas.Strokes = _inkRedoStack.Pop();
            return;
        }

        if (_redoStack.Count == 0) return;
        _undoStack.Push(new List<UIElement>(_drawnElements));
        _drawnElements = _redoStack.Pop();
        RebuildCanvas();
    }

    private void RebuildCanvas()
    {
        DrawCanvas.Children.Clear();
        foreach (var el in _drawnElements)
            DrawCanvas.Children.Add(el);

        // Rebuild mosaic block map (handles both Rectangle blocks and Path blocks)
        _mosaicBlockMap.Clear();
        foreach (var el in _drawnElements)
        {
            if (el is Rectangle r && r.Tag is string tag && tag.StartsWith("M"))
            {
                int col = (int)(Canvas.GetLeft(r) / _mosaicPixelSize);
                int row = (int)(Canvas.GetTop(r) / _mosaicPixelSize);
                _mosaicBlockMap[(col, row)] = r;
            }
            else if (el is Path p && p.Tag is string tag2 && tag2.StartsWith("M"))
            {
                var bounds = p.Data.Bounds;
                int col = (int)(bounds.X / _mosaicPixelSize);
                int row = (int)(bounds.Y / _mosaicPixelSize);
                _mosaicBlockMap[(col, row)] = p;
            }
        }
    }

    // ========== Save / Copy / Cancel ==========

    private void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        _settings = _settingsService.Load();
        var settingsWin = new SettingsWindow(_settings);
        settingsWin.Owner = this;
        if (settingsWin.ShowDialog() == true)
        {
            _settings = settingsWin.Settings;
            _settingsService.Save(_settings);
        }
    }
    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var result = RenderCombinedImage();
            if (result == null) return;

            // Save directly
            if (!System.IO.Directory.Exists(_settings.SavePath))
                System.IO.Directory.CreateDirectory(_settings.SavePath);

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var filename = $"Screenshot_{timestamp}{_settings.GetSaveExtension()}";
            var filepath = System.IO.Path.Combine(_settings.SavePath, filename);

            BitmapEncoder encoder = _settings.SaveFormat?.ToUpper() switch
            {
                "JPG" or "JPEG" => new JpegBitmapEncoder { QualityLevel = 95 },
                "BMP" => new BmpBitmapEncoder(),
                _ => new PngBitmapEncoder()
            };

            encoder.Frames.Add(BitmapFrame.Create(result));
            using (var stream = new System.IO.FileStream(filepath, System.IO.FileMode.Create))
                encoder.Save(stream);

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Save failed: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnCopy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var result = RenderCombinedImage();
            if (result != null)
            {
                System.Windows.Clipboard.SetImage(result);
            }
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Copy failed: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }


    // ========== Reset ==========

    private void FontFamilyBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FontFamilyBox.SelectedItem is ComboBoxItem item && item.Tag is string fontName)
        {
            _textFontFamily = fontName;
            ApplyFontToActive();
        }
    }

    private void BtnBold_Click(object sender, RoutedEventArgs e) { _textBold = BtnBold.IsChecked == true; ApplyFontToActive(); }
    private void BtnItalic_Click(object sender, RoutedEventArgs e) { _textItalic = BtnItalic.IsChecked == true; ApplyFontToActive(); }
    private void BtnUnderline_Checked(object sender, RoutedEventArgs e) { _textUnderline = true; ApplyFontToActive(); }
    private void BtnUnderline_Unchecked(object sender, RoutedEventArgs e) { _textUnderline = false; ApplyFontToActive(); }

    private void ApplyFontToActive()
    {
        if (_activeTextBox == null || !DrawCanvas.Children.Contains(_activeTextBox)) return;
        _activeTextBox.FontFamily = new System.Windows.Media.FontFamily(_textFontFamily);
        _activeTextBox.FontWeight = _textBold ? FontWeights.Bold : FontWeights.Normal;
        _activeTextBox.FontStyle = _textItalic ? FontStyles.Italic : FontStyles.Normal;
    }

    private void BtnReset_Click(object sender, RoutedEventArgs e)
    {
        _currentColor = Colors.Red;
        ColorPreview.Fill = new SolidColorBrush(Colors.Red);
        _mosaicPixelSize = 10;
        MosaicSizeSlider.Value = _mosaicPixelSize;
        _lastRegularSize = 3;
        _textFontFamily = "Microsoft YaHei";
        FontFamilyBox.SelectedIndex = 0;
        _textBold = false; BtnBold.IsChecked = false;
        _textItalic = false; BtnItalic.IsChecked = false;
        _textUnderline = false; BtnUnderline.IsChecked = false;
        if (_currentTool == EditTool.Mosaic)
        {
            _currentSize = 35;
            SizeSlider.Value = 35;
        }
        else if (_currentTool == EditTool.Text)
        {
            _currentSize = 24;
            SizeSlider.Value = 24;
        }
        else
        {
            _currentSize = 3;
            SizeSlider.Value = 3;
        }
        UpdateCanvasMode();
    }
    // ========== Render final image ==========

    private BitmapSource? RenderCombinedImage()
    {
        var source = (BitmapSource?)SourceImage.Source;
        if (source == null) return null;

        // No annotations: return source directly (bypass RenderTargetBitmap)
        if (_drawnElements.Count == 0 && InkCanvas.Strokes.Count == 0)
            return source;

        // Has annotations: render via DrawingVisual
        var dv = new System.Windows.Media.DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawImage(source, new Rect(0, 0, _imageW, _imageH));

            if (_mosaicBmp != null) dc.DrawImage(_mosaicBmp, new Rect(0, 0, _imageW, _imageH));

            foreach (var el in _drawnElements)
            {
                double left = Canvas.GetLeft(el);
                double top = Canvas.GetTop(el);
                var clone = CloneElement(el);
                if (clone != null)
                {
                    if (clone is Rectangle r)
                        dc.DrawRectangle(r.Fill, new System.Windows.Media.Pen(r.Stroke, r.StrokeThickness), new Rect(left, top, r.Width, r.Height));
                    else if (clone is Ellipse e)
                        dc.DrawEllipse(e.Fill, new System.Windows.Media.Pen(e.Stroke, e.StrokeThickness), new Point(left + e.Width/2, top + e.Height/2), e.Width/2, e.Height/2);
                    else if (clone is Line l)
                        dc.DrawLine(new System.Windows.Media.Pen(l.Stroke, l.StrokeThickness), new Point(l.X1, l.Y1), new Point(l.X2, l.Y2));
                    else if (clone is Path p)
                        dc.DrawGeometry(p.Fill, new System.Windows.Media.Pen(p.Stroke, p.StrokeThickness), p.Data);
                    else if (clone is TextBlock tblock)
                    {
                        var ft = new FormattedText(tblock.Text, System.Globalization.CultureInfo.CurrentCulture, System.Windows.FlowDirection.LeftToRight, new Typeface(tblock.FontFamily, tblock.FontStyle, tblock.FontWeight, tblock.FontStretch), tblock.FontSize, tblock.Foreground ?? Brushes.Black, 1.0);
                        ft.MaxTextWidth = Math.Min(double.IsNaN(tblock.MaxWidth) || double.IsInfinity(tblock.MaxWidth) ? _imageW : Math.Max(tblock.MaxWidth, 100), _imageW);
                        dc.DrawText(ft, new Point(left, top));
                    }
                }
            }

            // Draw ink strokes
            foreach (var stroke in InkCanvas.Strokes)
                stroke.Draw(dc);
        }

        var renderTarget = new RenderTargetBitmap(_imageW, _imageH, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        renderTarget.Render(dv);
        return renderTarget;
    }

    private UIElement? CloneElement(UIElement el)
    {
        if (el is Rectangle rect)
            return new Rectangle { Width = rect.Width, Height = rect.Height, Fill = rect.Fill?.Clone(), Stroke = rect.Stroke?.Clone(), StrokeThickness = rect.StrokeThickness };
        if (el is Ellipse ellipse)
            return new Ellipse { Width = ellipse.Width, Height = ellipse.Height, Fill = ellipse.Fill?.Clone(), Stroke = ellipse.Stroke?.Clone(), StrokeThickness = ellipse.StrokeThickness };
        if (el is Line line)
            return new Line { X1 = line.X1, Y1 = line.Y1, X2 = line.X2, Y2 = line.Y2, Stroke = line.Stroke?.Clone(), StrokeThickness = line.StrokeThickness };
        if (el is Path path)
            return new Path { Data = path.Data?.Clone(), Stroke = path.Stroke?.Clone(), Fill = path.Fill?.Clone(), StrokeThickness = path.StrokeThickness };
        if (el is TextBox tb)
            return new TextBox { Text = tb.Text, FontSize = tb.FontSize, Foreground = tb.Foreground?.Clone(), Width = tb.Width, IsReadOnly = true, BorderThickness = new Thickness(0) };
        if (el is Canvas canvas && canvas.Clip != null)
        {
            var clone = new Canvas { Clip = canvas.Clip.Clone() };
            foreach (UIElement child in canvas.Children)
            {
                if (child is Rectangle r)
                {
                    var rc = new Rectangle { Width = r.Width, Height = r.Height, Fill = r.Fill?.Clone() };
                    Canvas.SetLeft(rc, Canvas.GetLeft(r));
                    Canvas.SetTop(rc, Canvas.GetTop(r));
                    clone.Children.Add(rc);
                }
            }
            return clone;
        }
        if (el is TextBlock tbBlock)
            return new TextBlock { Text = tbBlock.Text, FontSize = tbBlock.FontSize, Foreground = tbBlock.Foreground?.Clone() };;
        return null;
    }

    // ========== Save success toast ==========

    private static void ShowSaveToast(string filepath)
    {
        var toast = new Window
        {
            Title = "",
            Width = 420,
            Height = 60,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = new SolidColorBrush(Color.FromArgb(220, 0x2D, 0x2D, 0x2D)),
            WindowStartupLocation = WindowStartupLocation.Manual,
            ShowInTaskbar = false,
            Topmost = true,
            ResizeMode = ResizeMode.NoResize,
            BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
            BorderThickness = new Thickness(1)
        };

        var grid = new Grid { Margin = new Thickness(12, 8, 12, 8) };
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition());

        var titleText = new System.Windows.Controls.TextBlock
        {
            Text = "截图已保存",
            Foreground = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)),
            FontSize = 14,
            FontWeight = FontWeights.Bold
        };
        Grid.SetRow(titleText, 0);
        grid.Children.Add(titleText);

        var pathText = new System.Windows.Controls.TextBlock
        {
            Text = System.IO.Path.GetFileName(filepath) + " → " + System.IO.Path.GetDirectoryName(filepath),
            Foreground = Brushes.LightGray,
            FontSize = 12,
            TextTrimming = System.Windows.TextTrimming.CharacterEllipsis
        };
        Grid.SetRow(pathText, 1);
        grid.Children.Add(pathText);

        toast.Content = grid;

        // Position at bottom-right of screen
        toast.Left = SystemParameters.WorkArea.Right - 440;
        toast.Top = SystemParameters.WorkArea.Bottom - 80;

        toast.Show();

        // Auto-close after 1.5 seconds
        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2),
            IsEnabled = true
        };
        timer.Tick += (_, _) => { timer.Stop(); toast.Close(); };
        timer.Start();
    }
    // ========== Keyboard shortcuts ==========

    private void EditWindow_KeyDown(object sender, KeyEventArgs e)
    {
        var mod = e.KeyboardDevice.Modifiers;
        if (e.Key == Key.Escape) BtnCancel_Click(sender, new RoutedEventArgs());
        else if (e.Key == Key.S && mod == ModifierKeys.Control) BtnSave_Click(sender, new RoutedEventArgs());
        else if (e.Key == Key.C && mod == ModifierKeys.Control) BtnCopy_Click(sender, new RoutedEventArgs());
        else if (e.Key == Key.Z && mod == ModifierKeys.Control) BtnUndo_Click(sender, new RoutedEventArgs());
        else if (e.Key == Key.Y && mod == ModifierKeys.Control) BtnRedo_Click(sender, new RoutedEventArgs());
    }


    // ========== Mosaic brush ==========

    private void MosaicSizeSlider_ValueChanged(object? sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _mosaicPixelSize = (int)MosaicSizeSlider.Value;
    }

    private void PaintMosaicBlockAt(Point pos)
    {
        double halfBrush = _currentSize / 2.0;
        int minCol = Math.Max(0, (int)((pos.X - halfBrush) / _mosaicPixelSize));
        int maxCol = (int)((pos.X + halfBrush) / _mosaicPixelSize);
        int minRow = Math.Max(0, (int)((pos.Y - halfBrush) / _mosaicPixelSize));
        int maxRow = (int)((pos.Y + halfBrush) / _mosaicPixelSize);

        for (int col = minCol; col <= maxCol; col++)
        {
            for (int row = minRow; row <= maxRow; row++)
            {
                double cx = col * _mosaicPixelSize + _mosaicPixelSize / 2.0;
                double cy = row * _mosaicPixelSize + _mosaicPixelSize / 2.0;
                double d2 = (cx - pos.X) * (cx - pos.X) + (cy - pos.Y) * (cy - pos.Y);
                if (d2 > halfBrush * halfBrush) continue;

                var key = (col, row);
                if (_mosaicStrokePositions.Contains(key))
                    continue;
                _mosaicStrokePositions.Add(key);
            }
        }
    }

    // ========== Zoom / Pan ==========

    private void ApplyCanvasLayout()
    {
        double chromeW = 16, chromeH = 135;
        double maxWinW = _screenW - 20;
        double maxWinH = _screenH - 20;

        // Canvas-first: window = canvas + chrome, cap only if exceeds screen
        double winW = _canvasW + chromeW;
        double winH = _canvasH + chromeH;
        double imgAspect = _imageW / (double)_imageH;

        if (winW > maxWinW || winH > maxWinH)
        {
            double maxCW = maxWinW - chromeW;
            double maxCH = maxWinH - chromeH;
            double maxAspect = maxCW / maxCH;
            if (imgAspect >= maxAspect)
            {
                _canvasW = maxCW;
                _canvasH = maxCW / imgAspect;
            }
            else
            {
                _canvasH = maxCH;
                _canvasW = maxCH * imgAspect;
            }
            _minZoom = _canvasW / _imageW;
            winW = _canvasW + chromeW;
            winH = _canvasH + chromeH;
        }
        if (winW < 800) winW = 800;
        if (winH < 300) winH = 300;

        CanvasContainer.Width = _canvasW;
        CanvasContainer.Height = _canvasH;
        CanvasContainer.MinWidth = _canvasW;
        CanvasContainer.MinHeight = _canvasH;
        Width = winW;
        Height = winH;

        // Button row (Row 0) height ~42px; keep window Top >= 0 so buttons stay visible
        WindowStartupLocation = WindowStartupLocation.Manual;
        Top = Math.Max(0, (_screenH - Height) / 2);
        Left = Math.Max(0, (_screenW - Width) / 2);

        // Set initial zoom: fill canvas (no blank space)
        _currentZoom = _minZoom;
        _zoom = _currentZoom;
        double scaledW = _imageW * _currentZoom;
        double scaledH = _imageH * _currentZoom;
        _panX = (_canvasW - scaledW) / 2;
        _panY = (_canvasH - scaledH) / 2;

        // Update ClampPan bounds
        ClampPan();
        ApplyZoomAndPan();
    }

    private void ClampPan()
    {
        double cw = _canvasW;
        double ch = _canvasH;
        if (cw <= 0 || ch <= 0 || _imageW <= 0 || _imageH <= 0) return;

        double scaledW = _imageW * _currentZoom;
        double scaledH = _imageH * _currentZoom;
        // LayoutTransform centers Grid: baseX = (cw - _canvasW) / 2
        double baseX = (cw - _canvasW) / 2;
        double baseY = (ch - _canvasH) / 2;

        double panX_min, panX_max;
        if (scaledW <= cw)
        {
            // Content fits horizontally -> centered, no panning
            panX_min = panX_max = (cw - scaledW) / 2 - baseX;
        }
        else
        {
            // Content overflows -> constrain: no empty space in viewport
            panX_max = -baseX;                   // contentLeft <= 0
            panX_min = cw - baseX - scaledW;     // contentRight >= cw
        }

        double panY_min, panY_max;
        if (scaledH <= ch)
        {
            panY_min = panY_max = (ch - scaledH) / 2 - baseY;
        }
        else
        {
            panY_max = -baseY;
            panY_min = ch - baseY - scaledH;
        }

        _panX = Math.Clamp(_panX, panX_min, panX_max);
        _panY = Math.Clamp(_panY, panY_min, panY_max);
    }

    private bool IsZoomedIn()
    {
        return _currentZoom > _minZoom * 1.01;
    }

    private void CanvasContainer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        double delta = e.Delta > 0 ? 1.1 : 1.0 / 1.1;
        double newZoom = Math.Clamp(_currentZoom * delta, _minZoom, 10.0);
        if (Math.Abs(newZoom - _currentZoom) < 0.001) return;

        var screenPos = e.GetPosition(CanvasContainer);
        double contentX = (screenPos.X - _panX) / _currentZoom;
        double contentY = (screenPos.Y - _panY) / _currentZoom;

        _panX = screenPos.X - contentX * newZoom;
        _panY = screenPos.Y - contentY * newZoom;

        _currentZoom = newZoom;
        _zoom = _currentZoom;
        ClampPan();
        ApplyZoomAndPan();
        e.Handled = true;

        if (!IsZoomedIn())
            Cursor = Cursors.Arrow;
    }

    private void EditWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && !_isSpaceDown && Keyboard.FocusedElement is not TextBox)
        {
            _isSpaceDown = true;
            if (IsZoomedIn())
                Cursor = Cursors.Hand;
            e.Handled = false;
        }
    }

    private void EditWindow_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            _isSpaceDown = false;
            if (_isPanning)
            {
                _isPanning = false;
                CanvasContainer.ReleaseMouseCapture();
            }
            Cursor = Cursors.Arrow;
            e.Handled = false;
        }
    }

    private void EditWindow_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(DrawCanvas);
        bool inside = pos.X >= 0 && pos.Y >= 0 && pos.X <= _imageW && pos.Y <= _imageH;
        if (!inside && !_isSpaceDown && !_isPanning)
            Cursor = Cursors.Arrow;
    }

    private void CanvasContainer_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_isSpaceDown && e.ChangedButton == MouseButton.Left && IsZoomedIn())
        {
            _isPanning = true;
            _panStart = e.GetPosition(CanvasContainer);
            _panStartX = _panX;
            _panStartY = _panY;
            CanvasContainer.CaptureMouse();
            Cursor = Cursors.ScrollAll;
            e.Handled = true;
        }
    }

    private void CanvasContainer_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_isPanning)
        {
            var pos = e.GetPosition(CanvasContainer);
            _panX = _panStartX + (pos.X - _panStart.X);
            _panY = _panStartY + (pos.Y - _panStart.Y);
            ClampPan();
            ApplyZoomAndPan();
            e.Handled = true;
        }
    }

    private void CanvasContainer_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isPanning && e.ChangedButton == MouseButton.Left)
        {
            _isPanning = false;
            CanvasContainer.ReleaseMouseCapture();
            Cursor = _isSpaceDown ? Cursors.Hand : Cursors.Arrow;
            e.Handled = true;
        }
    }

        private BitmapSource? RenderCompositeToBitmap()
    {
        // Render the on-screen Grid (SourceImage.Parent) directly
        // This includes SourceImage + DrawCanvas (with all drawn elements + flattened curves)
        // InkCanvas is collapsed during mosaic, but its strokes were flattened to DrawCanvas via FlattenInkStrokes
        var grid = SourceImage.Parent as Grid;
        if (grid == null) return null;

        var renderTarget = new RenderTargetBitmap(_imageW, _imageH, 96, 96, PixelFormats.Pbgra32);
        renderTarget.Render(grid);
        return renderTarget;
    }

    private void FinalizeMosaicStroke()
    {
        // Build strokeGeo from preview path using widened geometry for exact outline
        Geometry? strokeGeo = null;
        if (_mosaicPreviewFig != null)
        {
            var pts = new System.Collections.Generic.List<Point> { _mosaicPreviewFig.StartPoint };
            foreach (var seg in _mosaicPreviewFig.Segments)
                if (seg is LineSegment ls)
                    pts.Add(ls.Point);
            if (pts.Count == 1)
            {
                double r = _currentSize / 2.0;
                strokeGeo = new EllipseGeometry(pts[0], r, r);
            }
            else if (pts.Count >= 2)
            {
                var pathGeo = new PathGeometry();
                var fig = new PathFigure { StartPoint = pts[0], IsClosed = false, IsFilled = false };
                fig.Segments.Add(new PolyLineSegment(pts.Skip(1), true));
                pathGeo.Figures.Add(fig);
                var pen = new System.Windows.Media.Pen(System.Windows.Media.Brushes.Black, _currentSize)
                {
                    StartLineCap = System.Windows.Media.PenLineCap.Round,
                    EndLineCap = System.Windows.Media.PenLineCap.Round,
                    LineJoin = System.Windows.Media.PenLineJoin.Round
                };
                strokeGeo = pathGeo.GetWidenedPathGeometry(pen);
            }
        }

        if (_mosaicPreviewPath != null && DrawCanvas.Children.Contains(_mosaicPreviewPath))
        { DrawCanvas.Children.Remove(_mosaicPreviewPath); _mosaicPreviewPath = null; _mosaicPreviewFig = null; _mosaicPreviewGeo = null; }

        if (_mosaicStrokePositions.Count == 0) return;

        byte[]? px = null; int stride = 0, iw = 0, ih = 0;
        var src = SourceImage.Source as BitmapSource;
        if (src != null) { iw = src.PixelWidth; ih = src.PixelHeight; stride = iw*4; px = new byte[stride*ih]; src.CopyPixels(px, stride, 0); }

        var vis = new DrawingVisual();
        using (var dc = vis.RenderOpen())
        {
            if (strokeGeo != null) dc.PushClip(strokeGeo);
            foreach (var key in _mosaicStrokePositions)
            {
                var (col, row) = key;
                var c = GetAverageColor(col, row, _mosaicPixelSize, px, stride, iw, ih);
                dc.DrawRectangle(new SolidColorBrush(c), null, new Rect(col*_mosaicPixelSize, row*_mosaicPixelSize, _mosaicPixelSize, _mosaicPixelSize));
                _mosaicBlockMap[key] = null!;
            }
            if (strokeGeo != null) dc.Pop();
        }

        var nb = new RenderTargetBitmap(_imageW, _imageH, 96, 96, PixelFormats.Pbgra32);
        var comp = new DrawingVisual();
        using (var dc = comp.RenderOpen())
        {
            if (_mosaicBmp != null) dc.DrawImage(_mosaicBmp, new Rect(0, 0, _imageW, _imageH));
            dc.DrawDrawing(vis.Drawing);
        }
        nb.Render(comp);
        _mosaicBmp = nb;
        _mosaicImg!.Source = _mosaicBmp;
        _mosaicStrokePositions.Clear();
    }

    private Color GetAverageColor(int col, int row, int size, byte[]? pixels, int stride, int imgW, int imgH)
    {
        if (pixels == null) return Colors.Gray;

        int startX = Math.Clamp(col * size, 0, imgW - 1);
        int startY = Math.Clamp(row * size, 0, imgH - 1);
        int endX = Math.Clamp((col + 1) * size, 0, imgW);
        int endY = Math.Clamp((row + 1) * size, 0, imgH);

        long sumR = 0, sumG = 0, sumB = 0;
        int count = 0;
        for (int y = startY; y < endY; y++)
        {
            for (int x = startX; x < endX; x++)
            {
                int idx = y * stride + x * 4;
                if (idx + 2 < pixels.Length)
                {
                    sumB += pixels[idx];
                    sumG += pixels[idx + 1];
                    sumR += pixels[idx + 2];
                    count++;
                }
            }
        }

        if (count == 0) return Colors.Gray;
        return Color.FromRgb((byte)(sumR / count), (byte)(sumG / count), (byte)(sumB / count));
    }
}








