using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kh2RandoMac.Core;

public static class AppPaths
{
    public static string ConfigDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library", "Application Support", "kh2rando-mac");

    public static string CacheDir => Path.Combine(ConfigDir, "cache");
    public static string ConfigFile => Path.Combine(ConfigDir, "config.json");

    public static string DefaultWorkspace =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "KH2 Rando");
}

public class AppConfig
{
    public string? BottleName { get; set; }
    /// <summary>Mac path of "KINGDOM HEARTS -HD 1.5+2.5 ReMIX-" inside (or reachable from) the bottle.</summary>
    public string? GameDir { get; set; }
    /// <summary>The Sikarugir wrapper .app when the game runs through one; null for CrossOver.</summary>
    public string? WrapperApp { get; set; }

    private string _launcher = "Steam";
    /// <summary>"Steam" or "EGS", normalized on assignment so case/synonyms can't break comparisons.</summary>
    public string Launcher
    {
        get => _launcher;
        set => _launcher = NormalizeLauncher(value);
    }

    public string WorkspaceRoot { get; set; } = AppPaths.DefaultWorkspace;
    /// <summary>Language folder under Image/ containing the .hed/.pkg files (usually "en", "dt" on Steam, "jp" for Japan).</summary>
    public string Language { get; set; } = "en";

    /// <summary>Accepts steam/STEAM/epic/egs/... and returns the canonical value.</summary>
    public static string NormalizeLauncher(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "egs" or "epic" or "epic games" or "epic games launcher" => "EGS",
            _ => "Steam",
        };

    [JsonIgnore]
    public static JsonSerializerOptions JsonOptions { get; } = new() { WriteIndented = true };

    public static AppConfig Load(string? path = null)
    {
        path ??= AppPaths.ConfigFile;
        if (!File.Exists(path))
            return new AppConfig();
        try
        {
            return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path)) ?? new AppConfig();
        }
        catch (JsonException ex)
        {
            // A corrupt config must never brick the app: set it aside and start fresh.
            FileLog.Write($"config.json is corrupt ({ex.Message}); moving it to config.json.corrupt and using defaults");
            try { File.Move(path, path + ".corrupt", true); } catch { }
            return new AppConfig();
        }
    }

    public void Save(string? path = null)
    {
        path ??= AppPaths.ConfigFile;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // Atomic write so a crash mid-save can't truncate the config.
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOptions));
        File.Move(tmp, path, true);
    }
}
