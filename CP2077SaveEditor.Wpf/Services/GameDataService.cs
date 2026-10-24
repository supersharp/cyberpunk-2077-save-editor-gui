using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CP2077SaveEditor.Editing;
using CP2077SaveEditor.GameAssets;
using WolvenKit.RED4.Types;
using CP2077SaveEditor.Wpf.Views;

namespace CP2077SaveEditor.Wpf.Services;

/// <summary>
/// Real item names and icons read from the player's game installation. All game-file work (database, archives, texture
/// decoding) runs in short-lived child processes of this exe, once per game version, so the editor itself only reads a
/// small index and cached PNG icons. Everything works without it (readable record names, no icons).
/// </summary>
public sealed partial class GameDataService : ObservableObject
{
    public static GameDataService Instance { get; } = new();

    /// <summary>Child process switches handled in App.OnStartup: "&lt;switch&gt; &lt;game dir&gt;".</summary>
    public const string BuildIndexSwitch = "--build-game-data";
    public const string BuildIconsSwitch = "--build-game-icons";

    private const string IconsDoneMarker = "icons.done";

    private readonly ConcurrentDictionary<string, BitmapSource?> _icons = new();

    [ObservableProperty] private string? _gameDir;
    [ObservableProperty] private GameAssetIndex? _index;
    [ObservableProperty] private string _status = "Game data: not loaded";

    /// <summary>Raised on the UI thread when names, and later icons, become available.</summary>
    public event Action? Ready;

    private string? IconDir => GameDir == null ? null : Path.Combine(GameAssetIndex.CacheDir(GameDir), "icons");

    public bool IsCustomGameDir => !string.IsNullOrEmpty(AppSettings.Current.CustomGameDir);

    public async Task<bool> SetCustomGameDirAsync(string? path)
    {
        var resolved = path != null ? GameInstall.ResolveGameRoot(path) : null;
        if (resolved != null && !GameInstall.IsValid(resolved))
        {
            return false;
        }

        AppSettings.Current.CustomGameDir = resolved;
        AppSettings.Current.Save();

        _icons.Clear();
        Index = null;
        GameDir = null;
        Status = "Game data: reloading...";
        OnPropertyChanged(nameof(IsCustomGameDir));

        await InitializeAsync(promptIfNotFound: false);
        return true;
    }

    /// <summary>Opens a folder dialog to browse for the Cyberpunk 2077 installation, validates it, and applies it.</summary>
    public async Task<bool> PromptBrowseAndSetInstallDirAsync(System.Windows.Window? owner = null)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select Cyberpunk 2077 installation folder",
            InitialDirectory = GameDir ?? @"C:\Program Files (x86)\Steam\steamapps\common"
        };

        var result = (owner != null && owner.IsVisible) ? dialog.ShowDialog(owner) : dialog.ShowDialog();
        if (result != true || string.IsNullOrWhiteSpace(dialog.FolderName))
        {
            return false;
        }

        var resolved = GameInstall.ResolveGameRoot(dialog.FolderName);
        if (!GameInstall.IsValid(resolved))
        {
            ThemedDialog.ShowNotice(owner,
                $"The selected folder is not a valid Cyberpunk 2077 installation:\n\n{dialog.FolderName}\n\nA valid installation must contain 'r6\\cache\\tweakdb.bin' and 'archive\\pc\\content'.\n\nYou can set it manually at any time in the Extras menu.",
                "Invalid game directory");
            return false;
        }

        await SetCustomGameDirAsync(resolved);
        ThemedDialog.ShowNotice(owner,
            $"Cyberpunk 2077 installation configured:\n\n{resolved}\n\nGame assets and icons are being loaded.",
            "Install directory configured");
        return true;
    }

    /// <summary>Finds the game, loads or builds the name index, then makes sure icons are extracted (all background).</summary>
    public async Task InitializeAsync(bool promptIfNotFound = false)
    {
        GameDir = await Task.Run(() => GameInstall.Find(AppSettings.Current.CustomGameDir));
        OnPropertyChanged(nameof(IsCustomGameDir));
        if (GameDir == null)
        {
            Status = "Game data: Cyberpunk 2077 install not found (can be set manually in the Extras menu)";
            if (promptIfNotFound && Application.Current != null)
            {
                await Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    var owner = Application.Current.MainWindow;
                    var chooseLocation = ThemedDialog.ShowChoice(
                        owner,
                        "Could not locate your Cyberpunk 2077 installation automatically.\n\nWithout the game files, item names and icons cannot be displayed.\n\nWould you like to browse and select your Cyberpunk 2077 installation folder now?\n\n(You can also set it manually at any time in the Extras menu.)",
                        "Cyberpunk 2077 Install Not Found",
                        "SET LOCATION",
                        "IGNORE");

                    if (chooseLocation)
                    {
                        await PromptBrowseAndSetInstallDirAsync(owner);
                    }
                });
            }
            return;
        }

        try
        {
            var index = await Task.Run(() => GameAssetIndex.TryLoad(GameDir));
            if (index == null)
            {
                Status = "Game data: reading item names from your game files (one time)…";
                await RunChildAsync(BuildIndexSwitch, GameDir);
                index = GameAssetIndex.TryLoad(GameDir) ?? throw new InvalidDataException("the item index was not created");
            }
            Index = index;
            Ready?.Invoke();

            if (!File.Exists(Path.Combine(IconDir!, IconsDoneMarker)))
            {
                Status = "Game data: names ready, extracting item icons from your game files (one time)…";
                await RunChildAsync(BuildIconsSwitch, GameDir);
                foreach (var missing in _icons.Where(kv => kv.Value == null).Select(kv => kv.Key).ToList())
                {
                    _icons.TryRemove(missing, out _);
                }
                Ready?.Invoke();
            }
            Status = $"Game data: {index.Items.Count:N0} item names and icons from your game";
        }
        catch (Exception e)
        {
            App.Log(e);
            Status = "Game data: could not be read (" + e.Message + ")";
        }
    }

    /// <summary>The in-game name of an item, or the readable record name.</summary>
    public string NameOf(TweakDBID id) => Index?.Get(id)?.Name ?? ItemCatalog.DisplayName(id);

    /// <summary>Cached icon for an item (gendered for clothing), or null if there is none (yet).</summary>
    public async Task<BitmapSource?> GetIconAsync(TweakDBID id, bool female)
    {
        if (Index?.Get(id) is not { Icon: not null } asset || IconDir == null)
        {
            return null;
        }
        var key = IconKey((ulong)id, female && asset.IconFemale != null);
        if (_icons.TryGetValue(key, out var known))
        {
            return known;
        }

        var file = Path.Combine(IconDir, key + ".png");
        var image = await Task.Run(() => File.Exists(file) ? Load(file) : null);
        _icons[key] = image;
        return image;
    }

    private static string IconKey(ulong id, bool female) => female ? $"{id:X}_f" : $"{id:X}";

    // ---------------- child processes ----------------

    private static async Task RunChildAsync(string mode, string gameDir)
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("unknown executable path");
        var start = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        start.ArgumentList.Add(mode);
        start.ArgumentList.Add(gameDir);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("could not start the game data builder");
        var errors = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            throw new InvalidDataException(string.IsNullOrWhiteSpace(errors) ? $"builder exited with code {process.ExitCode}" : errors.Trim());
        }
    }

    /// <summary>Child process entry point (see App.OnStartup). Returns the process exit code.</summary>
    public static int RunBuilder(string mode, string gameDir)
    {
        try
        {
            if (mode == BuildIndexSwitch)
            {
                GameAssetIndex.Build(gameDir, ItemCatalog.AllRecordNames());
            }
            else
            {
                ExtractIcons(gameDir);
            }
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e.Message);
            App.Log(e);
            return 1;
        }
    }

    /// <summary>Decodes each needed atlas once and writes every item icon as a small PNG.</summary>
    private static void ExtractIcons(string gameDir)
    {
        var index = GameAssetIndex.TryLoad(gameDir) ?? throw new InvalidDataException("no item index");
        var iconDir = Path.Combine(GameAssetIndex.CacheDir(gameDir), "icons");
        Directory.CreateDirectory(iconDir);
        var atlases = new AtlasCache(gameDir);

        var jobs = new List<(string Key, IconRef Icon)>();
        foreach (var (id, asset) in index.Items)
        {
            if (asset.Icon != null) jobs.Add((IconKey(id, false), asset.Icon));
            if (asset.IconFemale != null) jobs.Add((IconKey(id, true), asset.IconFemale));
        }

        foreach (var group in jobs.GroupBy(j => (j.Icon.Archive, j.Icon.Texture)))
        {
            var pending = group.Where(j => !File.Exists(Path.Combine(iconDir, j.Key + ".png"))).ToList();
            if (pending.Count == 0)
            {
                continue;
            }
            var atlasPath = atlases.GetAtlasPng(pending[0].Icon);
            if (atlasPath == null)
            {
                continue;
            }
            var atlas = Load(atlasPath)!;
            foreach (var (key, icon) in pending)
            {
                var cropped = CropFlipped(atlas, icon);
                var file = Path.Combine(iconDir, key + ".png");
                using var stream = File.Create(file + ".tmp");
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(cropped));
                encoder.Save(stream);
                stream.Close();
                File.Move(file + ".tmp", file, overwrite: true);
            }
            File.Delete(atlasPath); // only the cropped icons are kept
        }
        File.WriteAllText(Path.Combine(iconDir, IconsDoneMarker), DateTime.Now.ToString("O"));
    }

    /// <summary>Decoded atlases are upside down: crop at the flipped V range, then flip the result back.</summary>
    private static BitmapSource CropFlipped(BitmapSource atlas, IconRef icon)
    {
        int w = atlas.PixelWidth, h = atlas.PixelHeight;
        var rect = new Int32Rect(
            (int)(icon.Left * w), (int)((1 - icon.Bottom) * h),
            Math.Max(1, (int)((icon.Right - icon.Left) * w)), Math.Max(1, (int)((icon.Bottom - icon.Top) * h)));
        rect.X = Math.Clamp(rect.X, 0, w - 1);
        rect.Y = Math.Clamp(rect.Y, 0, h - 1);
        rect.Width = Math.Min(rect.Width, w - rect.X);
        rect.Height = Math.Min(rect.Height, h - rect.Y);
        var flipped = new TransformedBitmap(new CroppedBitmap(atlas, rect), new ScaleTransform(1, -1));
        var frozen = new WriteableBitmap(flipped);
        frozen.Freeze();
        return frozen;
    }

    private static BitmapSource? Load(string path)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(path);
        image.EndInit();
        image.Freeze();
        return image;
    }
}
