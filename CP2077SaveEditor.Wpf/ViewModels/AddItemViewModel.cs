using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CP2077SaveEditor.Editing;
using CP2077SaveEditor.GameAssets;
using CP2077SaveEditor.Utils;
using CP2077SaveEditor.Wpf.Services;

namespace CP2077SaveEditor.Wpf.ViewModels;

/// <summary>Second-level filter chip (weapon kind, clothing slot, cyberware area); Key null = all.</summary>
public sealed record SubFilter(string? Key, string Name);

/// <summary>One item the Add item dialog will create.</summary>
public sealed record AddItemChoice(CatalogItem Item, long Quantity, ItemTier? Tier);

/// <summary>Backs the Add item dialog: searchable catalog of all known items, with grades grouped per item.</summary>
public sealed partial class AddItemViewModel : ObservableObject
{
    private static readonly SubFilter s_allSubs = new(null, "All");

    public AddItemViewModel(bool female, bool saveHasExpansion)
    {
        SaveHasExpansion = saveHasExpansion;
        Rows = BuildRows(female);
        foreach (var row in Rows)
        {
            row.IsLocked = row.IsExpansion && !saveHasExpansion && !AppSettings.Current.AllowExpansionItems;
        }
        _selectedFilter = Filters[0]; // must be set before the filter runs
        _selectedSubFilter = s_allSubs;
        CatalogView = CollectionViewSource.GetDefaultView(Rows);
        CatalogView.Filter = o => Matches((AddItemRow)o);
        RebuildSubFilters();
        UpdateSummary();
    }

    public IReadOnlyList<AddItemRow> Rows { get; }
    public ICollectionView CatalogView { get; }

    public IReadOnlyList<TypeFilter> Filters => InventoryViewModel.AllFilters;
    public ObservableCollection<SubFilter> SubFilters { get; } = new();

    /// <summary>The open save has Phantom Liberty; otherwise expansion items are locked unless allowed in Extras.</summary>
    public bool SaveHasExpansion { get; }

    [ObservableProperty]
    private TypeFilter _selectedFilter;

    [ObservableProperty]
    private SubFilter _selectedSubFilter;

    [ObservableProperty]
    private string _search = "";

    /// <summary>Also list records without an in-game name (templates, unused and removed items).</summary>
    [ObservableProperty]
    private bool _showUnnamed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStackable), nameof(CanHaveTier), nameof(CanAdd), nameof(HasSelection))]
    private AddItemRow? _selectedItem;

    [ObservableProperty]
    private long _quantity = 1;

    [ObservableProperty]
    private bool _setMaxTier = true;

    [ObservableProperty]
    private string _summary = "";

    public bool HasSelection => SelectedItem != null;
    public bool IsStackable => SelectedItem != null && InventoryEditor.IsStackable(SelectedItem.Variants[0].Item.Id);

    /// <summary>Tier 5++ only applies to items whose grade is rolled when created (graded variants carry their own).</summary>
    public bool CanHaveTier => SelectedItem is { HasGrades: false } row && InventoryEditor.HasTiers(row.Type) && row.Variants[0].Rank < 0;

    public bool CanAdd => SelectedItem is { IsLocked: false } row && (!row.HasGrades || row.Variants.Any(v => v.IsChecked));

    partial void OnSearchChanged(string value) => Refresh();
    partial void OnShowUnnamedChanged(bool value) => Refresh();
    partial void OnSelectedSubFilterChanged(SubFilter value) => Refresh();

    partial void OnSelectedFilterChanged(TypeFilter value)
    {
        RebuildSubFilters();
        Refresh();
    }

    partial void OnSelectedItemChanged(AddItemRow? oldValue, AddItemRow? newValue)
    {
        if (oldValue != null)
        {
            foreach (var v in oldValue.Variants) v.PropertyChanged -= OnVariantChanged;
        }
        if (newValue is { HasGrades: true })
        {
            // default to the best grade; the chips let the user pick others or all
            foreach (var v in newValue.Variants) v.IsChecked = false;
            newValue.Variants[^1].IsChecked = true;
            foreach (var v in newValue.Variants) v.PropertyChanged += OnVariantChanged;
        }
    }

    private void OnVariantChanged(object? sender, PropertyChangedEventArgs e) => OnPropertyChanged(nameof(CanAdd));

    [RelayCommand]
    private void SelectAllGrades()
    {
        if (SelectedItem is { HasGrades: true } row)
        {
            foreach (var v in row.Variants) v.IsChecked = true;
        }
    }

    [RelayCommand]
    private void SelectBestGrade()
    {
        if (SelectedItem is { HasGrades: true } row)
        {
            foreach (var v in row.Variants) v.IsChecked = false;
            row.Variants[^1].IsChecked = true;
        }
    }

    private void Refresh()
    {
        CatalogView.Refresh();
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        var shown = Rows.Count(Matches);
        Summary = $"{shown:N0} items";
    }

    private bool InMainFilter(AddItemRow row) =>
        (ShowUnnamed || !row.IsHidden) && (SelectedFilter.Types.Length == 0 || SelectedFilter.Types.Contains(row.FilterType));

    private void RebuildSubFilters()
    {
        SubFilters.Clear();
        SubFilters.Add(s_allSubs);
        if (SelectedFilter.Types.Length > 0)
        {
            foreach (var sub in Rows.Where(InMainFilter).GroupBy(r => r.SubName).Where(g => g.Key != "Other" && g.Key != "Misc").OrderBy(g => g.Key))
            {
                SubFilters.Add(new SubFilter(sub.Key, sub.Key));
            }
        }
        SelectedSubFilter = s_allSubs;
    }

    private bool Matches(AddItemRow row)
    {
        if (!InMainFilter(row) || (SelectedSubFilter.Key != null && row.SubName != SelectedSubFilter.Key))
        {
            return false;
        }
        if (string.IsNullOrWhiteSpace(Search))
        {
            return true;
        }
        var fields = new string[row.Variants.Count + 1];
        fields[0] = row.Name;
        for (var i = 0; i < row.Variants.Count; i++)
        {
            fields[i + 1] = row.Variants[i].Item.Record;
        }
        return FuzzySearch.MatchesAny(Search, fields);
    }

    public IReadOnlyList<AddItemChoice> Result
    {
        get
        {
            if (SelectedItem is not { } row || !CanAdd)
            {
                return Array.Empty<AddItemChoice>();
            }
            var quantity = IsStackable ? Quantity : 1;
            var tier = CanHaveTier && SetMaxTier ? ItemTier.Max : (ItemTier?)null;
            var variants = row.HasGrades ? row.Variants.Where(v => v.IsChecked) : row.Variants;
            return variants.Select(v => new AddItemChoice(v.Item, quantity, tier)).ToList();
        }
    }

    /// <summary>
    /// One row per item; records that are the same item in different grades (same name and kind, one record per
    /// grade, e.g. a cyberware implant from Tier 1 to Tier 5++) are grouped into a single row.
    /// </summary>
    private static List<AddItemRow> BuildRows(bool female)
    {
        var index = GameDataService.Instance.Index;
        var entries = ItemCatalog.Addable.Select(i => (Item: i, Asset: index?.Get(i.Id))).ToList();
        var rows = new List<AddItemRow>();
        foreach (var group in entries.GroupBy(e => (Name: e.Asset?.Name ?? e.Item.DisplayName, Filter: FilterType(e.Item, e.Asset),
                     Iconic: e.Asset?.Iconic == true)))
        {
            var sub = ItemCategories.SubKey(group.Key.Filter, group.First().Asset?.ItemType, group.First().Asset?.EquipArea);
            AddItemRow Row(IEnumerable<(CatalogItem Item, ItemAsset? Asset)> members) =>
                new(members.Select(m => new AddItemVariant(m.Item, m.Asset)).ToList(), group.Key.Name, group.Key.Filter, sub, index != null, female);

            // one record per grade; where several records share a grade, the current ("Advanced…", not "…Base") one
            // joins the group and the leftovers stay separate rows
            var graded = group.Where(m => ItemCategories.GradeRank(m.Asset?.Quality) >= 0 && !AddItemRow.IsTemplate(m.Item.Record))
                .GroupBy(m => ItemCategories.GradeRank(m.Asset?.Quality))
                .Select(g => g.OrderBy(m => m.Item.Record.StartsWith("Items.Advanced") ? 0 : 1).ThenBy(m => m.Item.Record.EndsWith("Base") ? 1 : 0).First())
                .OrderBy(m => ItemCategories.GradeRank(m.Asset?.Quality))
                .ToList();
            if (graded.Count < 2)
            {
                graded.Clear();
            }
            else
            {
                rows.Add(Row(graded));
            }
            rows.AddRange(group.Where(m => !graded.Contains(m)).Select(m => Row(new[] { m })));
        }
        rows.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
        return rows;
    }

    /// <summary>Type used for the filter chips: arm cyberware (Mantis Blades, Gorilla Arms…) is a weapon record but listed as cyberware.</summary>
    private static string FilterType(CatalogItem item, ItemAsset? asset) =>
        asset?.ItemType?.StartsWith("Cyb_") == true ? "Cyberware" : item.Type;
}

/// <summary>One grade of a catalog item (a separate item record).</summary>
public sealed partial class AddItemVariant : ObservableObject
{
    public AddItemVariant(CatalogItem item, ItemAsset? asset)
    {
        Item = item;
        Asset = asset;
        Rank = ItemCategories.GradeRank(asset?.Quality);
        Label = ItemCategories.GradeLabel(asset?.Quality);
    }

    public CatalogItem Item { get; }
    public ItemAsset? Asset { get; }
    public int Rank { get; }
    public string Label { get; }
    public string Tooltip => $"{Asset?.Quality ?? "Random"}  ·  {Item.Record}";

    [ObservableProperty]
    private bool _isChecked;
}

/// <summary>One catalog entry in the Add item dialog, with its in-game name, grades and (lazily loaded) icon.</summary>
public sealed partial class AddItemRow : ObservableObject
{
    private readonly bool _female;
    private BitmapSource? _icon;
    private bool _iconRequested;

    public AddItemRow(IReadOnlyList<AddItemVariant> variants, string name, string filterType, string? subKey, bool haveIndex, bool female)
    {
        Variants = variants;
        Name = name;
        FilterType = filterType;
        _female = female;
        SubName = ItemCategories.SubName(subKey);
        // with game data loaded, records the game has no name for are templates or unused items; NPC and base
        // records are templates too
        IsHidden = (haveIndex && variants.All(v => v.Asset?.Name == null)) || variants.All(v => IsTemplate(v.Item.Record));
        IsExpansion = variants.Any(v => v.Asset?.Expansion == true);
    }

    /// <summary>"Items.Base_Pulsar", "Items.NPC_Strong_Arms_Bennett", "Items.SandevistanBase": not meant for the player.</summary>
    public static bool IsTemplate(string record) =>
        record.StartsWith("Items.Base_") || record.StartsWith("Items.NPC_") || record.Contains("_NPC_") || record.EndsWith("Base");

    public IReadOnlyList<AddItemVariant> Variants { get; }
    public string Name { get; }
    public string Type => Variants[0].Item.Type;
    public string FilterType { get; }
    public string SubName { get; }
    public bool HasGrades => Variants.Count > 1;
    public bool IsHidden { get; }
    public bool IsExpansion { get; }

    /// <summary>Phantom Liberty item while the save does not have the expansion (and it is not allowed in Extras).</summary>
    public bool IsLocked { get; set; }

    public string Record => HasGrades ? $"{Variants.Count} grades  ·  {Variants[0].Item.Record}" : Variants[0].Item.Record;
    public string Details => HasGrades ? $"{Variants[0].Label} – {Variants[^1].Label}" : Variants[0].Rank >= 0 ? Variants[0].Label : "";
    public string Kind => SubName is "Other" or "Misc" ? TypeName : SubName;

    /// <summary>Readable item class: "ItemRecipe" -> "Recipe".</summary>
    public string TypeName => FilterType switch
    {
        "ItemRecipe" => "Recipe",
        "WeaponMod" => "Weapon mod",
        "WeaponItem" => "Weapon",
        "Part" => "Part",
        "General" or "Item" => "Item",
        _ => FilterType
    };

    public BitmapSource? Icon
    {
        get
        {
            if (!_iconRequested)
            {
                _iconRequested = true;
                _ = LoadAsync();
            }
            return _icon;
        }
    }

    public bool HasNoIcon => _iconRequested && _icon == null && !_loading;
    private bool _loading;

    private async Task LoadAsync()
    {
        _loading = true;
        // grades usually share one icon; use the first one that has any
        foreach (var v in Variants.Reverse())
        {
            _icon = await GameDataService.Instance.GetIconAsync(v.Item.Id, _female);
            if (_icon != null) break;
        }
        _loading = false;
        OnPropertyChanged(nameof(Icon));
        OnPropertyChanged(nameof(HasNoIcon));
    }
}
