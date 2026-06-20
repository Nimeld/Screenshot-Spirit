using System.IO;
using System.Text.Json;

namespace ScreenshotSpirit.Models;

public class AppSettings
{
    public bool AutoStart { get; set; } = false;
    public string SavePath { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "截图精灵");
    public string SaveFormat { get; set; } = "PNG"; // PNG, JPG, BMP

    public string GetSaveExtension()
    {
        return SaveFormat?.ToLower() switch
        {
            "jpg" => ".jpg",
            "jpeg" => ".jpg",
            "bmp" => ".bmp",
            _ => ".png"
        };
    }
}
