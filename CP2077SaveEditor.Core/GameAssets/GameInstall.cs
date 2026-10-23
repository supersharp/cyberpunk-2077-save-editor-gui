using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace CP2077SaveEditor.GameAssets;

/// <summary>Locates the Cyberpunk 2077 installation (Steam, GOG, Epic) for reading item names and icons.</summary>
public static class GameInstall
{
    public static string TweakDbPath(string gameDir) => Path.Combine(gameDir, "r6", "cache", "tweakdb.bin");
    public static string ContentDir(string gameDir) => Path.Combine(gameDir, "archive", "pc", "content");

    public static bool IsValid(string? gameDir) =>
        !string.IsNullOrWhiteSpace(gameDir) && File.Exists(TweakDbPath(gameDir)) && Directory.Exists(ContentDir(gameDir));

    /// <summary>Returns the first valid install found, or null.</summary>
    public static string? Find(string? customDir = null) => Candidates(customDir).FirstOrDefault(IsValid);

    /// <summary>
    /// Resolves the Cyberpunk 2077 root directory if the user selected a subfolder (e.g. bin\x64).
    /// </summary>
    public static string ResolveGameRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return path;
        var dir = path.TrimEnd('\\', '/');
        if (IsValid(dir)) return dir;

        var parent = Directory.GetParent(dir)?.FullName;
        if (parent != null && IsValid(parent)) return parent;

        var grandParent = Directory.GetParent(dir)?.Parent?.FullName;
        if (grandParent != null && IsValid(grandParent)) return grandParent;

        return dir;
    }

    private static IEnumerable<string> Candidates(string? customDir = null)
    {
        if (!string.IsNullOrWhiteSpace(customDir))
        {
            yield return ResolveGameRoot(customDir);
        }

        // Steam: every library listed in libraryfolders.vdf
        foreach (var steam in new[] { SteamPath(), @"C:\Program Files (x86)\Steam", @"C:\Program Files\Steam" }.Where(p => p != null).Distinct())
        {
            var vdf = Path.Combine(steam!, "steamapps", "libraryfolders.vdf");
            var libraries = new List<string> { steam! };
            if (File.Exists(vdf))
            {
                libraries.AddRange(Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\"").Select(m => m.Groups[1].Value.Replace(@"\\", @"\")));
            }
            foreach (var library in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                yield return Path.Combine(library, "steamapps", "common", "Cyberpunk 2077");
            }
        }

        // GOG
        if (OperatingSystem.IsWindows())
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\GOG.com\Games\1423049311");
            if (key?.GetValue("path") is string gog)
            {
                yield return gog;
            }
        }

        // Epic
        var manifests = @"C:\ProgramData\Epic\EpicGamesLauncher\Data\Manifests";
        if (Directory.Exists(manifests))
        {
            foreach (var file in Directory.EnumerateFiles(manifests, "*.item"))
            {
                string? location = null;
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(file));
                    if (doc.RootElement.TryGetProperty("DisplayName", out var name) && name.GetString()?.Contains("Cyberpunk", StringComparison.OrdinalIgnoreCase) == true &&
                        doc.RootElement.TryGetProperty("InstallLocation", out var loc))
                    {
                        location = loc.GetString();
                    }
                }
                catch (Exception)
                {
                    // unreadable manifest
                }
                if (location != null)
                {
                    yield return location;
                }
            }
        }

        foreach (var common in new[] { @"C:\GOG Games\Cyberpunk 2077", @"C:\Program Files\Epic Games\Cyberpunk2077", @"C:\Games\Cyberpunk 2077" })
        {
            yield return common;
        }
    }

    private static string? SteamPath()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        return (key?.GetValue("SteamPath") as string)?.Replace('/', '\\');
    }
}
