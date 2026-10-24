using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using WolvenKit.RED4.Types;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CP2077SaveEditor.Editing;
using CP2077SaveEditor.Utils;
using CP2077SaveEditor.GameAssets;
using CP2077SaveEditor.Wpf.Services;
using Microsoft.Win32;

namespace CP2077SaveEditor.Wpf.ViewModels;

/// <summary>Base for screens with a search box over a list.</summary>
public abstract partial class ListPageViewModel<TRow> : PageViewModel where TRow : class
{
    protected EditorSession? Session;

    protected ListPageViewModel()
    {
        RowsView = CollectionViewSource.GetDefaultView(Rows);
        RowsView.Filter = o => Shown((TRow)o);
    }

    private bool Shown(TRow row) => Include(row) && (string.IsNullOrWhiteSpace(Search) || Matches(row, Search.Trim()));

    /// <summary>Extra filter (e.g. a chip) applied on top of the search.</summary>
    protected virtual bool Include(TRow row) => true;

    public ObservableCollection<TRow> Rows { get; } = new();
    public ICollectionView RowsView { get; }

    [ObservableProperty]
    private string _search = "";

    [ObservableProperty]
    private string _summary = "";

    [ObservableProperty]
    private string? _notice;

    partial void OnSearchChanged(string value) => RefreshView();

    protected abstract bool Matches(TRow row, string search);

    protected void RefreshView()
    {
        RowsView.Refresh();
        var shown = Rows.Count(Shown);
        Summary = shown == Rows.Count ? $"{Rows.Count} entries" : $"{shown} of {Rows.Count} entries";
    }

    public override void OnSessionChanged(EditorSession? session)
    {
        base.OnSessionChanged(session);
        Session = session;
        Rows.Clear();
        Notice = null;
        if (session != null)
        {
            try
            {
                Load(session);
            }
            catch (Exception e)
            {
                Notice = "This data could not be read from the save: " + e.Message;
            }
        }
        RefreshView();
    }

    protected abstract void Load(EditorSession session);

    protected void Changed() => Session?.MarkChanged();
}

// ---------------- Appearance ----------------

public sealed partial class AppearanceEntryRow : ObservableObject
{
    public AppearanceEntryRow(AppearanceEntry entry)
    {
        Entry = entry;
        Reset();
    }

    public AppearanceEntry Entry { get; }
    public string Key => Entry.Key;

    [ObservableProperty] private string _definition = "";
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _resourcePath = "";

    public void Reset()
    {
        Definition = Entry.Entry.Definition.GetResolvedText() ?? "";
        Name = Entry.Entry.Name.GetResolvedText() ?? "";
        ResourcePath = Entry.Entry.Resource.DepotPath.GetResolvedText() ?? "";
    }
}

public sealed partial class AppearanceViewModel : ListPageViewModel<AppearanceEntryRow>
{
    public override string Title => "Appearance";
    public override string Glyph => "";

    [ObservableProperty] private bool _isBodyMale;
    [ObservableProperty] private bool _isVoiceMale;
    [ObservableProperty] private AppearanceEntryRow? _selectedEntry;

    protected override bool Matches(AppearanceEntryRow row, string search) => FuzzySearch.MatchesAny(search, row.Key, row.Name, row.Definition, row.ResourcePath);

    protected override void Load(EditorSession session)
    {
        IsBodyMale = session.Save.Appearance.BodyGender == AppearanceGender.Male;
        IsVoiceMale = session.Save.Appearance.VoiceTone == AppearanceGender.Male;
        foreach (var entry in AppearanceEditor.Entries(session.Save))
        {
            Rows.Add(new AppearanceEntryRow(entry));
        }
    }

    private void Reload()
    {
        var session = Session;
        OnSessionChanged(session);
    }

    [RelayCommand]
    private void SetBodyGender(string gender)
    {
        if (Session == null) return;
        var target = gender == "Male" ? AppearanceGender.Male : AppearanceGender.Female;
        if (Session.Save.Appearance.BodyGender == target) return;
        Session.Save.Appearance.BodyGender = target; // asks for confirmation (resets appearance to default)
        if (Session.Save.Appearance.BodyGender == target) Changed();
        Reload();
    }

    [RelayCommand]
    private void SetVoiceTone(string gender)
    {
        if (Session == null) return;
        var target = gender == "Male" ? AppearanceGender.Male : AppearanceGender.Female;
        if (Session.Save.Appearance.VoiceTone == target) return;
        Session.Save.Appearance.VoiceTone = target; // asks for confirmation
        if (Session.Save.Appearance.VoiceTone == target) Changed();
        Reload();
    }

    [RelayCommand]
    private void SavePreset()
    {
        if (Session == null) return;
        var dialog = new SaveFileDialog { Filter = "Character preset|*" + AppearanceEditor.PresetExtension, FileName = Session.Slot.FolderName };
        if (dialog.ShowDialog() == true)
        {
            File.WriteAllText(dialog.FileName, AppearanceEditor.SavePreset(Session.Save));
            Notice = "Preset saved to " + dialog.FileName;
        }
    }

    [RelayCommand]
    private void LoadPreset()
    {
        if (Session == null) return;
        var dialog = new OpenFileDialog
        {
            Filter = $"Character preset|*{AppearanceEditor.PresetExtension};*{AppearanceEditor.LegacyPresetExtension}"
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            AppearanceEditor.LoadPreset(Session.Save, File.ReadAllText(dialog.FileName), Path.GetExtension(dialog.FileName));
            Changed();
            Reload();
            Notice = "Preset loaded: " + Path.GetFileName(dialog.FileName);
        }
        catch (Exception e)
        {
            UserPrompt.Notice("The preset could not be loaded: " + e.Message, "Load preset");
        }
    }

    [RelayCommand]
    private void ApplyEntry()
    {
        if (SelectedEntry is not { } row) return;
        if (AppearanceEditor.Apply(row.Entry, row.Definition, row.Name, row.ResourcePath))
        {
            Changed();
        }
    }

    [RelayCommand]
    private void ResetEntry() => SelectedEntry?.Reset();
}

// ---------------- Vehicles ----------------

public sealed partial class VehicleRow : ObservableObject
{
    private readonly VehiclesViewModel _owner;
    private BitmapSource? _icon;
    private bool _iconRequested;

    public VehicleRow(VehiclesViewModel owner, VehicleEntry entry)
    {
        _owner = owner;
        Record = entry.Record;
        var asset = GameDataService.Instance.Index?.Get((TweakDBID)entry.Record);
        DisplayName = asset?.Name ?? entry.DisplayName;
        Kind = asset?.ItemType switch { "Bike" => "Motorcycle", null => "Vehicle", var t => t };
        Manufacturer = Readable(asset?.Manufacturer);
        IsExpansion = asset?.Expansion == true;
        _unlocked = entry.Unlocked;
        _favorite = entry.Favorite;
    }

    public string Record { get; }
    public string DisplayName { get; }
    public string Kind { get; }
    public string Manufacturer { get; }
    public bool IsExpansion { get; }
    public string Subtitle => string.IsNullOrEmpty(Manufacturer) ? Kind : $"{Manufacturer}  ·  {Kind}";

    [ObservableProperty] private bool _unlocked;
    [ObservableProperty] private bool _favorite;

    partial void OnUnlockedChanged(bool value) => _owner.SetUnlocked(this, value);
    partial void OnFavoriteChanged(bool value) => _owner.SetFavorite(this, value);

    /// <summary>Vehicle picture from the game files (the one shown in the call-vehicle menu), loaded on first use.</summary>
    public BitmapSource? Icon
    {
        get
        {
            if (!_iconRequested)
            {
                _iconRequested = true;
                _ = LoadIconAsync();
            }
            return _icon;
        }
    }

    private async Task LoadIconAsync()
    {
        _icon = await GameDataService.Instance.GetIconAsync((TweakDBID)Record, false);
        OnPropertyChanged(nameof(Icon));
    }

    /// <summary>"ArchMotorcycles" -> "Arch Motorcycles".</summary>
    private static string Readable(string? s) =>
        string.IsNullOrEmpty(s) ? "" : System.Text.RegularExpressions.Regex.Replace(s.Replace('_', ' '), "(?<=[a-z])(?=[A-Z])", " ");
}

public sealed partial class VehiclesViewModel : ListPageViewModel<VehicleRow>
{
    private bool _bulk;

    public VehiclesViewModel()
    {
        _selectedFilter = Filters[0];
        // names, pictures and newer vehicles come from the game data, which may finish loading after the save opened
        GameDataService.Instance.Ready += () => OnSessionChanged(Session);
    }

    public override string Title => "Vehicles";
    public override string Glyph => "";

    public IReadOnlyList<string> Filters { get; } = new[] { "All", "Unlocked", "Locked", "Favorites", "Cars", "Motorcycles" };

    [ObservableProperty] private string _selectedFilter;

    partial void OnSelectedFilterChanged(string value) => RefreshView();

    protected override bool Include(VehicleRow row) => SelectedFilter switch
    {
        "Unlocked" => row.Unlocked,
        "Locked" => !row.Unlocked,
        "Favorites" => row.Favorite,
        "Cars" => row.Kind == "Car",
        "Motorcycles" => row.Kind == "Motorcycle",
        _ => true
    };

    protected override bool Matches(VehicleRow row, string search) =>
        FuzzySearch.MatchesAny(search, row.DisplayName, row.Record, row.Manufacturer, row.Kind);

    protected override void Load(EditorSession session)
    {
        var vehicles = WorldEditor.Vehicles(session.Save, GameDataService.Instance.Index?.VehicleRecords);
        if (vehicles == null)
        {
            Notice = "This save has no garage data yet. Unlock at least one vehicle in the game first.";
            return;
        }
        var index = GameDataService.Instance.Index;
        _bulk = true;
        foreach (var v in vehicles)
        {
            // with game data loaded, skip list entries that are not vehicles in this game version (unless already owned)
            if (index == null || v.Unlocked || index.Get((TweakDBID)v.Record) != null)
            {
                Rows.Add(new VehicleRow(this, v));
            }
        }
        _bulk = false;
    }

    internal void SetUnlocked(VehicleRow row, bool unlocked)
    {
        if (_bulk || Session == null) return;
        WorldEditor.SetVehicleUnlocked(Session.Save, row.Record, unlocked);
        Changed();
        if (SelectedFilter is "Unlocked" or "Locked") RefreshView();
    }

    internal void SetFavorite(VehicleRow row, bool favorite)
    {
        if (_bulk || Session == null) return;
        WorldEditor.SetVehicleFavorite(Session.Save, row.Record, favorite);
        Changed();
    }

    private List<VehicleRow> ShownRows() => RowsView.Cast<VehicleRow>().ToList();

    [RelayCommand]
    private void UnlockAll()
    {
        var rows = ShownRows().Where(r => !r.Unlocked).ToList();
        if (rows.Count == 0 || Session == null) return;
        _bulk = true;
        foreach (var row in rows)
        {
            row.Unlocked = true;
            WorldEditor.SetVehicleUnlocked(Session.Save, row.Record, true);
        }
        _bulk = false;
        Changed();
        RefreshView();
        Notice = $"Unlocked {rows.Count} vehicle(s).";
    }

    [RelayCommand]
    private void LockAll()
    {
        var rows = ShownRows().Where(r => r.Unlocked).ToList();
        if (rows.Count == 0 || Session == null) return;
        if (!UserPrompt.Confirm($"Lock {rows.Count} vehicle(s)? Locked vehicles disappear from your garage until unlocked again.", "Lock vehicles")) return;
        _bulk = true;
        foreach (var row in rows)
        {
            row.Unlocked = false;
            WorldEditor.SetVehicleUnlocked(Session.Save, row.Record, false);
        }
        _bulk = false;
        Changed();
        RefreshView();
        Notice = $"Locked {rows.Count} vehicle(s).";
    }

    [RelayCommand]
    private void Preview(VehicleRow? row)
    {
        if (row != null) Views.ImagePreviewWindow.Show(null, row.Icon, row.DisplayName, row.Record);
    }
}

// ---------------- Quest facts ----------------

public sealed partial class FactRow : ObservableObject
{
    private readonly QuestFactsViewModel _owner;

    public FactRow(QuestFactsViewModel owner, FactEntryInfo info)
    {
        _owner = owner;
        Info = info;
        _value = info.Entry.Value;
        OriginalValue = info.Entry.Value;
        Category = CategoryOf(info);
    }

    public FactEntryInfo Info { get; }
    public string Name => Info.Name;
    public bool IsKnown => Info.IsKnown;
    public string Category { get; }

    /// <summary>Value when the save was opened, to list and undo edits.</summary>
    public long OriginalValue { get; }
    public bool IsEdited => Value != OriginalValue;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEdited))]
    private long _value;

    partial void OnValueChanged(long value)
    {
        if (Info.Entry.Value != (uint)value)
        {
            Info.Entry.Value = (uint)value;
            _owner.MarkChanged();
        }
    }

    /// <summary>Groups facts by their quest prefix: q101_ main story, sq0xx side jobs, mq/ma/sts gigs and minor activities…</summary>
    private static string CategoryOf(FactEntryInfo info)
    {
        if (!info.IsKnown) return "Unnamed";
        var n = info.Name.TrimStart().ToLowerInvariant();
        if (System.Text.RegularExpressions.Regex.IsMatch(n, @"^(q30\d|sq30\d|mq30\d|ep1|wst_ep1|sts_ep1)")) return "Phantom Liberty";
        if (n.Contains("romance") || n.Contains("lover") || n.Contains("relationship")) return "Romance";
        if (System.Text.RegularExpressions.Regex.IsMatch(n, @"^q\d{3}")) return "Main story";
        if (System.Text.RegularExpressions.Regex.IsMatch(n, @"^sq\d{3}")) return "Side jobs";
        if (System.Text.RegularExpressions.Regex.IsMatch(n, @"^(mq\d|ma_|sts_|ss_|mws_|wst_)")) return "Gigs & minor";
        return "Other";
    }
}

public sealed partial class QuestFactsViewModel : ListPageViewModel<FactRow>
{
    public override string Title => "Quest facts";
    public override string Glyph => "";

    public QuestFactsViewModel() => _selectedFilter = Filters[0];

    [ObservableProperty] private FactRow? _selectedFact;
    [ObservableProperty] private string _newFactName = "";
    [ObservableProperty] private long _newFactValue = 1;

    public IReadOnlyList<string> Filters { get; } = new[] { "All", "Main story", "Side jobs", "Gigs & minor", "Romance", "Phantom Liberty", "Other", "Unnamed", "Edited" };

    [ObservableProperty] private string _selectedFilter;

    /// <summary>Known fact names matching what is typed in the "add or set" box, including facts the save does not have yet.</summary>
    public ObservableCollection<string> Suggestions { get; } = new();

    [ObservableProperty] private string? _selectedSuggestion;

    partial void OnSelectedFilterChanged(string value) => RefreshView();

    partial void OnNewFactNameChanged(string value)
    {
        Suggestions.Clear();
        var text = value.Trim();
        if (text.Length < 2 || WorldEditor.KnownFactNames.Any(n => n == text)) return;
        var ranked = WorldEditor.KnownFactNames
            .Select(n => (Name: n.Trim(), Score: FuzzySearch.Score(n, text)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Name.Length)
            .Take(8);
        foreach (var item in ranked)
        {
            Suggestions.Add(item.Name);
        }
    }

    partial void OnSelectedSuggestionChanged(string? value)
    {
        if (value == null) return;
        NewFactName = value;
        // show the current value if the save already has this fact
        if (Rows.FirstOrDefault(r => r.Name.Trim() == value) is { } row) NewFactValue = row.Value;
    }

    protected override bool Include(FactRow row) => SelectedFilter switch
    {
        "All" => true,
        "Edited" => row.IsEdited,
        var category => row.Category == category
    };

    protected override bool Matches(FactRow row, string search) => FuzzySearch.MatchesAny(search, row.Name, row.Category);

    [RelayCommand]
    private void UndoEdits()
    {
        var edited = Rows.Where(r => r.IsEdited).ToList();
        foreach (var row in edited) row.Value = row.OriginalValue;
        RefreshView();
        Notice = edited.Count == 0 ? "No edited facts." : $"Restored {edited.Count} fact(s) to their original values.";
    }

    protected override void Load(EditorSession session)
    {
        foreach (var fact in WorldEditor.Facts(session.Save).OrderBy(f => !f.IsKnown).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
        {
            Rows.Add(new FactRow(this, fact));
        }
    }

    internal void MarkChanged() => Changed();

    private void Reload() => OnSessionChanged(Session);

    [RelayCommand]
    private void RemoveFact()
    {
        if (SelectedFact is not { } row) return;
        WorldEditor.RemoveFact(row.Info);
        Rows.Remove(row);
        Changed();
        RefreshView();
    }

    [RelayCommand]
    private void AddFact()
    {
        if (Session == null || string.IsNullOrWhiteSpace(NewFactName)) return;
        if (!WorldEditor.SetFact(Session.Save, NewFactName.Trim(), (uint)NewFactValue))
        {
            UserPrompt.Notice($"\"{NewFactName}\" is not a known quest fact name.", "Add fact");
            return;
        }
        Changed();
        Search = NewFactName.Trim();
        Reload();
    }

    [RelayCommand]
    private void SecretEnding()
    {
        if (Session == null) return;
        WorldEditor.EnableSecretEnding(Session.Save);
        Changed();
        Reload();
        Notice = "Secret ending enabled (sq032_johnny_friend = 1).";
    }

    [RelayCommand]
    private void AllRomanceable()
    {
        if (Session == null) return;
        WorldEditor.MakeAllRomanceable(Session.Save);
        Changed();
        Reload();
        Notice = "Judy, River, Panam and Kerry are now romanceable.";
    }

    [RelayCommand]
    private void Export()
    {
        if (Session == null) return;
        var dialog = new SaveFileDialog { Filter = "JSON|*.json", FileName = Session.Slot.FolderName + "-facts.json" };
        if (dialog.ShowDialog() == true)
        {
            File.WriteAllText(dialog.FileName, WorldEditor.ExportFacts(Session.Save));
            Notice = "Facts exported to " + dialog.FileName;
        }
    }
}

// ---------------- Extras ----------------

public sealed partial class ExtrasViewModel : PageViewModel
{
    private EditorSession? _session;

    public ExtrasViewModel()
    {
        GameDataService.Instance.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(GameDataService.GameDir) or nameof(GameDataService.IsCustomGameDir))
            {
                OnPropertyChanged(nameof(InstallStatus));
                OnPropertyChanged(nameof(IsCustomGameDir));
            }
        };
    }

    public override string Title => "Extras";
    public string InstallStatus => GameDataService.Instance.GameDir ?? "Not found (can be set manually in the Extras menu)";
    public bool IsCustomGameDir => GameDataService.Instance.IsCustomGameDir;

    [RelayCommand]
    private async Task BrowseInstallDir()
    {
        if (await GameDataService.Instance.PromptBrowseAndSetInstallDirAsync())
        {
            OnPropertyChanged(nameof(InstallStatus));
            OnPropertyChanged(nameof(IsCustomGameDir));
        }
    }

    [RelayCommand]
    private async Task ResetInstallDir()
    {
        await GameDataService.Instance.SetCustomGameDirAsync(null);
        OnPropertyChanged(nameof(InstallStatus));
        OnPropertyChanged(nameof(IsCustomGameDir));
        UserPrompt.Notice("Game install location reset to automatic detection.", "Install directory");
    }
    public override string Glyph => "";

    [ObservableProperty] private bool _hasExtraWardrobe;
    [ObservableProperty] private string? _notice;

    // save flags
    private bool _loadingFlags;
    [ObservableProperty] private bool _isModded;
    [ObservableProperty] private string _moddedDetails = "";
    public ObservableCollection<string> AdditionalContent { get; } = new();
    [ObservableProperty] private int _expansionItemCount;

    /// <summary>Editor setting: let Add item add Phantom Liberty items to saves without the expansion.</summary>
    [ObservableProperty] private bool _allowExpansionItems = AppSettings.Current.AllowExpansionItems;

    partial void OnAllowExpansionItemsChanged(bool value)
    {
        if (value && !UserPrompt.Confirm("Phantom Liberty items in a save without the expansion can crash the game or fail to load.\n\n" +
                                         "Allow adding them anyway?", "Phantom Liberty items"))
        {
            AllowExpansionItems = false;
            return;
        }
        AppSettings.Current.AllowExpansionItems = value;
        AppSettings.Current.Save();
    }

    public override void OnSessionChanged(EditorSession? session)
    {
        base.OnSessionChanged(session);
        _session = session;
        Notice = null;
        HasExtraWardrobe = session != null && WorldEditor.HasExtraWardrobeSlots(session.Save);
        RefreshFlags();
    }

    private void RefreshFlags()
    {
        AdditionalContent.Clear();
        if (_session == null)
        {
            return;
        }
        var state = SaveFlags.ReadModded(_session.Save);
        _loadingFlags = true;
        IsModded = state.IsModded;
        _loadingFlags = false;
        ModdedDetails = $"metadata isModded: {Text(state.MetadataModded)}  ·  mod.io mods listed: {state.ModioModCount}  ·  sav.dat ModdingSystem: {Text(state.SaveDataModded)}";
        foreach (var id in SaveFlags.AdditionalContent(_session.Save))
        {
            AdditionalContent.Add(id);
        }
        ExpansionItemCount = SaveFlags.ExpansionItems(_session.Save).Count;

        static string Text(bool? b) => b == null ? "not present" : b.Value ? "modded" : "clean";
    }

    partial void OnIsModdedChanged(bool value)
    {
        if (_loadingFlags || _session == null) return;
        SaveFlags.SetModded(_session.Save, value);
        _session.MarkChanged();
        RefreshFlags();
        Notice = value ? "Save marked as modded." : "Modded markers cleared (metadata and sav.dat).";
    }

    [RelayCommand]
    private void RemoveContent(string id)
    {
        if (_session == null) return;
        if (!UserPrompt.Confirm($"Remove \"{id}\" from the save's expansion list?\n\nThe expansion's quests and world data stay in the save; " +
                                "the game may refuse to load it or behave oddly. A backup is made when you overwrite.", "Expansion flag"))
        {
            return;
        }
        if (SaveFlags.RemoveAdditionalContent(_session.Save, id))
        {
            _session.MarkChanged();
            Notice = $"\"{id}\" removed from the expansion list.";
        }
        RefreshFlags();
    }

    [RelayCommand]
    private void RemoveExpansionItems()
    {
        if (_session == null || ExpansionItemCount == 0) return;
        if (!UserPrompt.Confirm($"Remove {ExpansionItemCount} Phantom Liberty item(s) from all containers?", "Phantom Liberty items")) return;
        var removed = SaveFlags.RemoveExpansionItems(_session.Save);
        _session.MarkChanged();
        RefreshFlags();
        Notice = $"Removed {removed} Phantom Liberty item(s).";
    }

    private void Run(Func<SaveFileHelper, string> action)
    {
        if (_session == null) return;
        try
        {
            Notice = action(_session.Save);
            _session.MarkChanged();
        }
        catch (Exception e)
        {
            Notice = "Failed: " + e.Message;
        }
    }

    [RelayCommand] private void UnlockFastTravel() => Run(s => $"{WorldEditor.UnlockAllFastTravel(s)} fast travel points unlocked.");
    [RelayCommand] private void MakeVulnerable() => Run(s => $"Removed {WorldEditor.MakePlayerVulnerable(s)} invulnerability override(s).");
    [RelayCommand] private void ClearWardrobeBlacklist() => Run(s => { WorldEditor.ClearWardrobeBlacklist(s); return "Wardrobe blacklist cleared."; });
}
