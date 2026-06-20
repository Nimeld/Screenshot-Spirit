using System.IO;
using System.Text.Json;
using ScreenshotSpirit.Models;

namespace ScreenshotSpirit.Services;

public class SettingsService
{
    private static readonly string SettingsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ScreenshotSpirit", "settings.json");

    private AppSettings? _cache;

    public AppSettings Load()
    {
        if (_cache != null) return _cache;

        try
        {
            if (File.Exists(SettingsFile))
            {
                var json = File.ReadAllText(SettingsFile);
                _cache = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                return _cache;
            }
        }
        catch { }

        _cache = new AppSettings();
        return _cache;
    }

    public void Save(AppSettings settings)
    {
        _cache = settings;
        var dir = Path.GetDirectoryName(SettingsFile);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsFile, json);
    }
}
