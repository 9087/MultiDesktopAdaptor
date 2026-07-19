using System.Text.Json;
using MultiDesktopAdaptor.Models;

namespace MultiDesktopAdaptor.Services;

public static class ConfigurationService
{
    private static readonly string FilePath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MultiDesktopAdaptor",
        "settings.json");

    public static AppConfiguration Load()
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(FilePath)!;
            System.IO.Directory.CreateDirectory(dir);

            if (!System.IO.File.Exists(FilePath))
                return new AppConfiguration();

            var json = System.IO.File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<AppConfiguration>(json) ?? new AppConfiguration();
        }
        catch
        {
            return new AppConfiguration();
        }
    }

    public static void Save(AppConfiguration config)
    {
        var dir = System.IO.Path.GetDirectoryName(FilePath)!;
        System.IO.Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        System.IO.File.WriteAllText(FilePath, json);
    }
}
