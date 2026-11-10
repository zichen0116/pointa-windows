using System.Globalization;
using System.Text.Json;
using Microsoft.Win32;

namespace Pointa.Windows;

internal sealed class AppSettings
{
    public string Language { get; set; } = CultureInfo.CurrentUICulture.Name.StartsWith("zh") ? "zh" : "en";
    public string? ScreenName { get; set; }
    public bool StartWithWindows { get; set; }
    public string LaserColor { get; set; } = "#FF6475";
    public int LaserSize { get; set; } = 16;
    public int TrailMs { get; set; } = 500;
    public string Smoothing { get; set; } = "standard";

    public AppSettings WithGeneralPreferences(string language, bool startup)
    {
        var next = (AppSettings)MemberwiseClone();
        next.Language = language;
        next.StartWithWindows = startup;
        return next;
    }

    public static AppSettings Load(string directory)
    {
        var path = Path.Combine(directory, "settings.json");
        if (!File.Exists(path)) return new();
        var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path))
            ?? throw new InvalidDataException("Settings file is empty.");
        if (settings.Language is not ("zh" or "en") || settings.LaserSize is < 6 or > 40 ||
            settings.TrailMs is < 0 or > 1200 || settings.Smoothing is not ("low" or "standard" or "high") ||
            !System.Text.RegularExpressions.Regex.IsMatch(settings.LaserColor, "^#[0-9a-fA-F]{6}$"))
            throw new InvalidDataException("Settings file contains unsupported values.");
        return settings;
    }

    public void Save(string directory)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "settings.json");
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, overwrite: true);
    }

    public void ApplyStartup()
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (StartWithWindows) key.SetValue("Pointa", $"\"{Environment.ProcessPath}\" --background");
        else key.DeleteValue("Pointa", throwOnMissingValue: false);
    }

    public string T(string chinese, string english) => Language == "zh" ? chinese : english;
}
