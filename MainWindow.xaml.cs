using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using ScreenshotSpirit.Services;
using ScreenshotSpirit.Views;

namespace ScreenshotSpirit;

public partial class MainWindow : Window
{
    private HotkeyService? _hotkeyService;
    private Window? _hintWindow;
    private bool _isCapturing;

    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Hide window completely after HWND is created
        WindowState = WindowState.Minimized;
        ShowInTaskbar = false;
        Visibility = Visibility.Hidden;
        Left = -30000;
        Top = -30000;
        Width = 0;
        Height = 0;

        _hotkeyService = new HotkeyService(this, 0x0003, 0x41);
        _hotkeyService.HotkeyPressed += OnHotkeyPressed;

        ShowStartupHint();
    }

    private void ShowStartupHint()
    {
        _hintWindow = new Window
        {
            Title = "截图精灵",
            Width = 420,
            Height = 280,
            WindowStyle = WindowStyle.ThreeDBorderWindow,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ShowInTaskbar = false,
            ResizeMode = ResizeMode.NoResize,
            Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x2D, 0x2D, 0x2D)),
            Foreground = System.Windows.Media.Brushes.White,
            Owner = this
        };

        var grid = new System.Windows.Controls.Grid { Margin = new Thickness(20) };
        grid.RowDefinitions.Add(new RowDefinition { Height = System.Windows.GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = System.Windows.GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = System.Windows.GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = System.Windows.GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = System.Windows.GridLength.Auto });

        var titleBlock = new System.Windows.Controls.TextBlock
        {
            Text = "截图精灵",
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            Foreground = System.Windows.Media.Brushes.White
        };
        Grid.SetRow(titleBlock, 0);
        grid.Children.Add(titleBlock);

        var hintBlock = new System.Windows.Controls.TextBlock
        {
            Text = "按 Ctrl+Alt+A 截图\n双击选区截取全屏\n选取区域后自动弹出编辑工具",
            FontSize = 13,
            Foreground = System.Windows.Media.Brushes.LightGray,
            Margin = new Thickness(0, 8, 0, 0),
            LineHeight = 20
        };
        Grid.SetRow(hintBlock, 1);
        grid.Children.Add(hintBlock);

        var btnPanel = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };

        var settingsBtn = new System.Windows.Controls.Button
        {
            Content = "设置",
            Width = 90,
            Height = 30,
            Margin = new Thickness(0, 0, 8, 0),
            Cursor = Cursors.Hand
        };
        settingsBtn.Click += (_, _) =>
        {
            var s = new SettingsService().Load();
            var win = new SettingsWindow(s);
            win.Owner = _hintWindow;
            if (win.ShowDialog() == true)
                new SettingsService().Save(win.Settings);
        };
        btnPanel.Children.Add(settingsBtn);

        var folderBtn = new System.Windows.Controls.Button
        {
            Content = "📂 截图文件夹",
            Width = 120,
            Height = 30,
            Cursor = Cursors.Hand
        };
        folderBtn.Click += (_, _) =>
        {
            var s = new SettingsService().Load();
            if (System.IO.Directory.Exists(s.SavePath))
                System.Diagnostics.Process.Start("explorer.exe", s.SavePath);
        };
        btnPanel.Children.Add(folderBtn);

        Grid.SetRow(btnPanel, 2);
        grid.Children.Add(btnPanel);

        var closeHint = new System.Windows.Controls.TextBlock
        {
            Text = "关闭后程序仍在后台运行\n按 Ctrl+Alt+A 随时截图",
            FontSize = 11,
            Foreground = System.Windows.Media.Brushes.Gray,
            Margin = new Thickness(0, 12, 0, 0),
            LineHeight = 16
        };
        Grid.SetRow(closeHint, 3);
        grid.Children.Add(closeHint);

        _hintWindow.Content = grid;
        _hintWindow.Show();
    }

    private void OnHotkeyPressed()
    {
        if (_isCapturing) return;
        _isCapturing = true;
        if (_hintWindow != null)
        {
            _hintWindow.Close();
            _hintWindow = null;
        }

        var screenshot = CaptureService.CaptureFullVirtualScreen();

        Dispatcher.Invoke(() =>
        {
            var captureWindow = new CaptureWindow(screenshot);
            captureWindow.Owner = Application.Current.MainWindow;
            captureWindow.ShowDialog();
            _isCapturing = false;
        });
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
        WindowState = WindowState.Minimized;

        ShowCloseNotification();
    }

    private void ShowCloseNotification()
    {
        var tip = new Window
        {
            Width = 360,
            Height = 50,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb(220, 0x2D, 0x2D, 0x2D)),
            WindowStartupLocation = WindowStartupLocation.Manual,
            ShowInTaskbar = false,
            Topmost = true,
            ResizeMode = ResizeMode.NoResize,
            BorderBrush = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb(80, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Owner = this
        };
        tip.Left = SystemParameters.WorkArea.Right - 380;
        tip.Top = SystemParameters.WorkArea.Bottom - 70;

        tip.Content = new System.Windows.Controls.TextBlock
        {
            Text = "截图精灵正在后台运行 (Ctrl+Alt+A)",
            Foreground = System.Windows.Media.Brushes.LightGray,
            FontSize = 13,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8)
        };

        tip.Show();

        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2),
            IsEnabled = true
        };
        timer.Tick += (_, _) => { timer.Stop(); tip.Close(); };
        timer.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        _hotkeyService?.Dispose();
        _hintWindow?.Close();
        base.OnClosed(e);
    }
}

