using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CP2077SaveEditor.Utils;
using CP2077SaveEditor.Wpf.Services;

namespace CP2077SaveEditor.Wpf.ViewModels;

public sealed partial class SaveSlotViewModel : ObservableObject
{
    public SaveSlotViewModel(SaveSlot slot) => Slot = slot;

    public SaveSlot Slot { get; }

    public string Title => Slot.Kind switch
    {
        SaveKind.Manual => "Manual save",
        SaveKind.Auto => "Autosave",
        SaveKind.Quick => "Quicksave",
        SaveKind.PointOfNoReturn => "Point of no return",
        SaveKind.EndGame => "End game",
        _ => Slot.FolderName
    };

    public string Quest => string.IsNullOrEmpty(Slot.Quest) ? "—" : Slot.Quest;
    public string Character => string.Join("  ·  ", new[] { $"LVL {Slot.Level}", $"CRED {Slot.StreetCred}", Slot.LifePath }).Trim();
    public string When => Slot.SavedAt.ToString("dd MMM yyyy  HH:mm");
    public string PlayTime => $"{(int)Slot.PlayTime.TotalHours}h {Slot.PlayTime.Minutes:00}m played";
    public string Patch => string.IsNullOrEmpty(Slot.BuildPatch) ? "?" : Slot.BuildPatch;

    [ObservableProperty]
    private ImageSource? _thumbnail;

    public async Task LoadThumbnailAsync()
    {
        if (Thumbnail != null || Slot.ScreenshotPath == null)
        {
            return;
        }

        Thumbnail = await Task.Run(() =>
        {
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = 400;
                image.StreamSource = new MemoryStream(File.ReadAllBytes(Slot.ScreenshotPath));
                image.EndInit();
                image.Freeze();
                return (ImageSource?)image;
            }
            catch (Exception)
            {
                return null;
            }
        });
    }
}

public sealed partial class SaveBrowserViewModel : PageViewModel
{
    private readonly Func<SaveSlot, Task> _open;

    public SaveBrowserViewModel(Func<SaveSlot, Task> open)
    {
        _open = open;
        SlotsView = CollectionViewSource.GetDefaultView(Slots);
        SlotsView.Filter = o => Matches((SaveSlotViewModel)o);
        IsEnabled = true;
    }

    public override string Title => "Saves";
    public override string Glyph => "";
    public override bool RequiresSave => false;

    public string Root { get; private set; } = SaveLibrary.DefaultRoot;

    public ObservableCollection<SaveSlotViewModel> Slots { get; } = new();
    public ICollectionView SlotsView { get; }

    [ObservableProperty]
    private string _filter = "";

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private string _summary = "";

    partial void OnFilterChanged(string value) => SlotsView.Refresh();

    private bool Matches(SaveSlotViewModel s)
    {
        if (SelectedSource != "All" && s.Slot.Source != SelectedSource)
        {
            return false;
        }
        if (string.IsNullOrWhiteSpace(Filter))
        {
            return true;
        }
        return FuzzySearch.MatchesAny(Filter, s.Slot.FolderName, s.Quest, s.Title, s.Slot.LifePath, s.Character, s.Patch);
    }

    /// <summary>Source chips: All, Game saves, My library and each added folder.</summary>
    public ObservableCollection<string> Sources { get; } = new();

    [ObservableProperty]
    private string _selectedSource = "All";

    [ObservableProperty]
    private string? _notice;

    partial void OnSelectedSourceChanged(string value)
    {
        SlotsView.Refresh();
        OnPropertyChanged(nameof(CanRemoveFolder));
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        IsScanning = true;
        try
        {
            var slots = await SaveLibrary.ScanAllAsync(Root, AppSettings.Current.LibraryFolders);
            Slots.Clear();
            foreach (var slot in slots)
            {
                Slots.Add(new SaveSlotViewModel(slot));
            }

            var selected = SelectedSource;
            Sources.Clear();
            foreach (var source in new[] { "All", SaveLibrary.GameSourceName, SaveLibrary.LibrarySourceName }
                         .Concat(AppSettings.Current.LibraryFolders.Select(SaveLibrary.SourceName)).Distinct())
            {
                Sources.Add(source);
            }
            SelectedSource = Sources.Contains(selected) ? selected : "All";
            SlotsView.Refresh();

            var game = slots.Count(s => s.IsGameSave);
            Summary = $"{game} game saves  ·  {slots.Count - game} in your library  ·  {Root}";

            foreach (var slot in Slots)
            {
                _ = slot.LoadThumbnailAsync();
            }
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand]
    private Task OpenAsync(SaveSlotViewModel? slot) => slot == null ? Task.CompletedTask : _open(slot.Slot);

    [RelayCommand]
    private void OpenFolder() => System.Diagnostics.Process.Start("explorer.exe", Root);

    [RelayCommand]
    private void ShowInExplorer(SaveSlotViewModel? slot)
    {
        if (slot != null) System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{slot.Slot.SavePath}\"");
    }

    /// <summary>Copies save folders (picked anywhere on disk) into the editor's library.</summary>
    [RelayCommand]
    private async Task ImportAsync()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Pick one or more save folders (each holds a sav.dat)", Multiselect = true };
        if (dialog.ShowDialog() != true) return;
        var imported = 0;
        foreach (var folder in dialog.FolderNames)
        {
            if (!File.Exists(Path.Combine(folder, "sav.dat")))
            {
                UserPrompt.Notice($"{folder} has no sav.dat, so it is not a Cyberpunk 2077 save folder.", "Import save");
                continue;
            }
            Run(() => SaveLibrary.CopyToLibrary(folder));
            imported++;
        }
        if (imported > 0)
        {
            Notice = $"Imported {imported} save(s) into your library ({SaveLibrary.LibraryRoot}).";
            SelectedSource = SaveLibrary.LibrarySourceName;
            await RefreshAsync();
        }
    }

    /// <summary>Shows the saves in another folder (e.g. a backup drive) without copying them.</summary>
    [RelayCommand]
    private async Task AddFolderAsync()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Pick a folder that contains save folders" };
        if (dialog.ShowDialog() != true || AppSettings.Current.LibraryFolders.Contains(dialog.FolderName, StringComparer.OrdinalIgnoreCase)) return;
        AppSettings.Current.LibraryFolders.Add(dialog.FolderName);
        AppSettings.Current.Save();
        Notice = $"Added {dialog.FolderName}.";
        await RefreshAsync();
        SelectedSource = SaveLibrary.SourceName(dialog.FolderName);
    }

    /// <summary>Stops showing an added folder (its saves are not touched).</summary>
    [RelayCommand]
    private async Task RemoveFolderAsync()
    {
        var folder = AppSettings.Current.LibraryFolders.FirstOrDefault(f => SaveLibrary.SourceName(f) == SelectedSource);
        if (folder == null) return;
        AppSettings.Current.LibraryFolders.Remove(folder);
        AppSettings.Current.Save();
        Notice = $"{folder} is no longer shown. Its saves were not changed.";
        SelectedSource = "All";
        await RefreshAsync();
    }

    public bool CanRemoveFolder => AppSettings.Current.LibraryFolders.Any(f => SaveLibrary.SourceName(f) == SelectedSource);

    [RelayCommand]
    private async Task CopyToGameAsync(SaveSlotViewModel? slot)
    {
        if (slot == null) return;
        var target = Run(() => SaveLibrary.CopyToGame(slot.Slot.Folder, Root, move: false));
        if (target != null)
        {
            Notice = $"Copied {slot.Slot.FolderName} to the game's saves as {Path.GetFileName(target)}. It is now in Load Game.";
            await RefreshAsync();
        }
    }

    [RelayCommand]
    private async Task MoveToGameAsync(SaveSlotViewModel? slot)
    {
        if (slot == null || slot.Slot.IsGameSave) return;
        if (!UserPrompt.Confirm($"Move {slot.Slot.FolderName} into the game's save folder? It is removed from {slot.Slot.Source}.", "Move to game saves")) return;
        var target = Run(() => SaveLibrary.CopyToGame(slot.Slot.Folder, Root, move: true));
        if (target != null)
        {
            Notice = $"Moved {slot.Slot.FolderName} to the game's saves as {Path.GetFileName(target)}.";
            await RefreshAsync();
        }
    }

    [RelayCommand]
    private async Task CopyToLibraryAsync(SaveSlotViewModel? slot)
    {
        if (slot == null) return;
        var target = Run(() => SaveLibrary.CopyToLibrary(slot.Slot.Folder));
        if (target != null)
        {
            Notice = $"Copied {slot.Slot.FolderName} to your library.";
            await RefreshAsync();
        }
    }

    private static string? Run(Func<string> action)
    {
        try
        {
            return action();
        }
        catch (Exception e)
        {
            App.Log(e);
            UserPrompt.Notice("That did not work: " + e.Message, "Saves");
            return null;
        }
    }
}
