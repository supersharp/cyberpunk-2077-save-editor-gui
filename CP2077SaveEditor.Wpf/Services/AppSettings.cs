using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace CP2077SaveEditor.Wpf.Services;

/// <summary>Editor preferences, stored as JSON next to the game data cache.</summary>
public sealed class AppSettings
{
    private static readonly string s_path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CP2077 Save Editor", "settings.json");

    public static AppSettings Current { get; } = Load();

    /// <summary>Custom Cyberpunk 2077 install directory set by the user; null to auto-detect.</summary>
    public string? CustomGameDir { get; set; }

    /// <summary>Let Phantom Liberty items be added to saves without the expansion (the game may crash).</summary>
    public bool AllowExpansionItems { get; set; }

    /// <summary>Extra folders shown in the save browser next to the game's own save folder.</summary>
    public List<string> LibraryFolders { get; set; } = new();

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(s_path))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(s_path)) ?? new AppSettings();
            }
        }
        catch (Exception e)
        {
            App.Log(e);
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(s_path)!);
            File.WriteAllText(s_path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e)
        {
            App.Log(e);
        }
    }
}
