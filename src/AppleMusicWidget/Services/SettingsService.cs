using System.IO;
using System.Text.Json;
using AppleMusicWidget.Models;

namespace AppleMusicWidget.Services;

public sealed class SettingsService
{
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AppleMusicWidget");
    private static readonly string FilePath = Path.Combine(Dir, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public WidgetSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<WidgetSettings>(File.ReadAllText(FilePath));
                if (s is not null) return s;
            }
        }
        catch { /* corrupted settings -> defaults */ }
        return new WidgetSettings();
    }

    public void Save(WidgetSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch { /* non-fatal */ }
    }
}
