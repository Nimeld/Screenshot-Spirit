using System.Windows;
using ScreenshotSpirit.Models;
using ScreenshotSpirit.Services;

namespace ScreenshotSpirit.Views;

public partial class SettingsWindow : Window
{
    public AppSettings Settings { get; private set; }

    public SettingsWindow(AppSettings current)
    {
        InitializeComponent();
        Settings = new AppSettings
        {
            AutoStart = current.AutoStart,
            SavePath = current.SavePath,
            SaveFormat = current.SaveFormat
        };

        ChkAutoStart.IsChecked = Settings.AutoStart;
        TxtSavePath.Text = Settings.SavePath;

        switch (Settings.SaveFormat?.ToUpper())
        {
            case "JPG": CmbFormat.SelectedIndex = 1; break;
            case "BMP": CmbFormat.SelectedIndex = 2; break;
            default: CmbFormat.SelectedIndex = 0; break;
        }
    }

    private void BtnBrowse_Click(object sender, RoutedEventArgs e)
    {
        // Use Windows Forms FolderBrowserDialog via explicit reference
        var dialog = new System.Windows.Forms.FolderBrowserDialog();
        dialog.SelectedPath = TxtSavePath.Text;
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            TxtSavePath.Text = dialog.SelectedPath;
        }
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        Settings.AutoStart = ChkAutoStart.IsChecked == true;
        Settings.SavePath = TxtSavePath.Text;
        Settings.SaveFormat = ((System.Windows.Controls.ComboBoxItem)CmbFormat.SelectedItem)?.Content?.ToString() ?? "PNG";

        AutoStartService.SetEnabled(Settings.AutoStart);

        if (!System.IO.Directory.Exists(Settings.SavePath))
            System.IO.Directory.CreateDirectory(Settings.SavePath);

        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
