using System.Windows;
using ScreenshotSpirit.Services;

namespace ScreenshotSpirit;

public partial class App : Application
{
    private static System.Threading.EventWaitHandle? _singleInstanceEvent;

    protected override void OnStartup(StartupEventArgs e)
    {
        bool createdNew;
        _singleInstanceEvent = new System.Threading.EventWaitHandle(
            false,
            System.Threading.EventResetMode.AutoReset,
            "ScreenshotSpirit_SingleInstance",
            out createdNew);

        if (!createdNew)
        {
            // Another instance is already running — exit silently
            System.Environment.Exit(0);
            return;
        }

        base.OnStartup(e);

        var settingsService = new SettingsService();
        var settings = settingsService.Load();

        if (!System.IO.Directory.Exists(settings.SavePath))
            System.IO.Directory.CreateDirectory(settings.SavePath);

        AutoStartService.SetEnabled(settings.AutoStart);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstanceEvent?.Dispose();
        base.OnExit(e);
    }
}
