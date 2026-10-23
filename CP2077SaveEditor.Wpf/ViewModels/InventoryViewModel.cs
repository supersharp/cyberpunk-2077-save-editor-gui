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
using CP2077SaveEditor.Utils;
using CP2077SaveEditor.Wpf.Services;
using WolvenKit.RED4.Save;
using WolvenKit.RED4.Save.Classes;
using WolvenKit.RED4.Types;

namespace CP2077SaveEditor.Wpf.ViewModels;

public sealed record ContainerOption(SubInventory Container, string Name)
{
    public string Label => $"{Name}  ({Container.Items.Count})";
}

/// <summary>Type filter chip; "All" matches everything.</summary>
public sealed record TypeFilter(string Name, string[] Types);

public sealed partial class ItemRowViewModel : ObservableObject
{
    private readonly InventoryViewModel _owner;

    public ItemRowViewModel(InventoryViewModel owner, ItemData item, string? equippedIn)
    {
        _owner = owner;
        Item = item;
        EquippedIn = equippedIn;
        Name = GameDataService.Instance.NameOf(item.ItemInfo.ItemId.Id);
        Record = ItemCatalog.RecordName(item.ItemInfo.ItemId.Id) ?? $"{(ulong)item.ItemInfo.ItemId.Id:X}";
        Type = ItemCatalog.Type(item.ItemInfo.ItemId.Id);
        _quantity = item.Quantity;
        RefreshTier();
    }

    public ItemData Item { get; }
    public string Name { get; }

    private BitmapSource? _icon;
    private bool _iconRequested;

    /// <summary>Item icon from the game files, loaded the first time a row asks for it (only visible rows do).</summary>
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
        _icon = await GameDataService.Instance.GetIconAsync(Item.ItemInfo.ItemId.Id, _owner.IsFemale);
        OnPropertyChanged(nameof(Icon));
    }
    public string Record { get; }
    public string Type { get; }
    public string? EquippedIn { get; }
    public bool IsEquipped => EquippedIn != null;
    public bool HasQuantity => Item.HasQuantity();
    public bool HasTiers => InventoryEditor.HasTiers(Type);
    public bool IsQuestItem
    {
        get => Item.Flags.HasFlag(ItemFlag.IsQuestItem);
        set
        {
            if (value != IsQuestItem)
            {
                InventoryEditor.SetQuestFlag(Item, value);
                OnPropertyChanged();
                _owner.Changed();
            }
        }
    }

    [ObservableProperty]
    private long _quantity;

    /// <summary>Money is stored as the quantity of the "Eurodollars" item: show it as cash, not as a count.</summary>
    public bool IsMoney => Item.ItemInfo.ItemId.Id == PlayerEditor.MoneyItem;
    public string QuantityText => IsMoney ? $"€$ {Quantity:N0}" : $"{Quantity:N0}";

    partial void OnQuantityChanged(long value)
    {
        OnPropertyChanged(nameof(QuantityText));
        if (Item.Quantity != (uint)value)
        {
            Item.Quantity = (uint)value;
            _owner.Changed();
        }
    }

    [ObservableProperty]
    private ItemTier? _tier;

    public string TierText => Tier?.ToString() ?? (HasTiers ? "Random roll" : "");

    partial void OnTierChanged(ItemTier? value) => OnPropertyChanged(nameof(TierText));

    public void RefreshTier() => Tier = HasTiers ? InventoryEditor.GetTier(_owner.Save!, Item) : null;
}

public sealed partial class InventoryViewModel : PageViewModel
{
    private EditorSession? _session;

    public InventoryViewModel()
    {
        GameDataService.Instance.Ready += LoadItems; // swap in real names and icons once the game data is ready
        ItemsView = CollectionViewSource.GetDefaultView(Items);
        ItemsView.Filter = o => Matches((ItemRowViewModel)o);
        _selectedFilter = Filters[0];
    }

    public override string Title => "Inventory";
    public override string Glyph => "";

    internal SaveFileHelper? Save => _session?.Save;

    /// <summary>Clothing icons are gendered; use V's body type.</summary>
    internal bool IsFemale { get; private set; }

    public ObservableCollection<ContainerOption> Containers { get; } = new();
    public ObservableCollection<ItemRowViewModel> Items { get; } = new();
    public ICollectionView ItemsView { get; }

    public IReadOnlyList<TypeFilter> Filters => AllFilters;

    public static IReadOnlyList<TypeFilter> AllFilters { get; } = new[]
    {
        new TypeFilter("All", Array.Empty<string>()),
        new TypeFilter("Weapons", new[] { "Weapon", "WeaponItem" }),
        new TypeFilter("Clothing", new[] { "Clothing" }),
        new TypeFilter("Cyberware", new[] { "Cyberware" }),
        new TypeFilter("Consumables", new[] { "Consumable", "Gadget" }),
        new TypeFilter("Mods & parts", new[] { "WeaponMod", "Part" }),
        new TypeFilter("Other", new[] { "General", "Item", "ItemRecipe", "Other" }),
    };

    public IReadOnlyList<int> TierOptions { get; } = new[] { 1, 2, 3, 4, 5 };

    [ObservableProperty]
    private ContainerOption? _selectedContainer;

    [ObservableProperty]
    private TypeFilter _selectedFilter;

    [ObservableProperty]
    private string _search = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private ItemRowViewModel? _selectedItem;

    [ObservableProperty]
    private string _summary = "";

    public bool HasSelection => SelectedItem != null;

    partial void OnSearchChanged(string value) => RefreshView();
    partial void OnSelectedFilterChanged(TypeFilter value) => RefreshView();
    partial void OnSelectedContainerChanged(ContainerOption? value) => LoadItems();

    public override void OnSessionChanged(EditorSession? session)
    {
        base.OnSessionChanged(session);
        _session = session;
        Containers.Clear();
        Items.Clear();
        if (session == null)
        {
            return;
        }

        try
        {
            IsFemale = session.Save.Appearance.BodyGender == AppearanceGender.Female;
        }
        catch (Exception)
        {
            IsFemale = false;
        }

        foreach (var sub in session.Save.GetInventoriesContainer().SubInventories)
        {
            Containers.Add(new ContainerOption(sub, InventoryEditor.ContainerName(sub.InventoryId)));
        }
        SelectedContainer = Containers.FirstOrDefault();
    }

    internal void Changed() => _session?.MarkChanged();

    private void LoadItems()
    {
        Items.Clear();
        SelectedItem = null;
        if (SelectedContainer == null || _session == null)
        {
            return;
        }

        var equipped = InventoryEditor.EquippedItems(_session.Save);
        foreach (var item in SelectedContainer.Container.Items)
        {
            var area = equipped.FirstOrDefault(e => e.Key.Id == item.ItemInfo.ItemId.Id && (uint)e.Key.RngSeed == (uint)item.ItemInfo.ItemId.RngSeed).Value;
            Items.Add(new ItemRowViewModel(this, item, area));
        }
        RefreshView();
    }

    private bool Matches(ItemRowViewModel row)
    {
        if (SelectedFilter.Types.Length > 0 && !SelectedFilter.Types.Contains(row.Type))
        {
            return false;
        }
        return string.IsNullOrWhiteSpace(Search)
               || FuzzySearch.MatchesAny(Search, row.Name, row.Record, row.EquippedIn, row.TierText);
    }

    private void RefreshView()
    {
        ItemsView.Refresh();
        var shown = Items.Count(Matches);
        Summary = shown == Items.Count ? $"{Items.Count} items" : $"{shown} of {Items.Count} items";
    }

    [RelayCommand]
    private void SetTier(string tier)
    {
        if (SelectedItem is not { HasTiers: true } row || _session == null)
        {
            return;
        }
        var current = row.Tier ?? new ItemTier(1, 0);
        var target = int.Parse(tier) is var t and >= 1 and <= 5 ? new ItemTier(t, t == 5 ? current.Plus : Math.Min(current.Plus, 1)) : current;
        ApplyTier(row, target);
    }

    [RelayCommand]
    private void SetPlus(string plus)
    {
        if (SelectedItem is not { HasTiers: true } row)
        {
            return;
        }
        var current = row.Tier ?? new ItemTier(5, 0);
        var p = int.Parse(plus);
        // "++" only exists at tier 5
        ApplyTier(row, new ItemTier(p == 2 ? 5 : current.Tier, p));
    }

    private void ApplyTier(ItemRowViewModel row, ItemTier tier)
    {
        try
        {
            InventoryEditor.SetTier(_session!.Save, row.Item, tier);
            row.RefreshTier();
            Changed();
        }
        catch (Exception e)
        {
            UserPrompt.Notice("Could not change the tier: " + e.Message, "Inventory");
        }
    }

    [RelayCommand]
    private void RemoveSelected()
    {
        if (SelectedItem is not { } row || SelectedContainer == null || _session == null)
        {
            return;
        }
        if (row.IsEquipped && !UserPrompt.Confirm($"{row.Name} is equipped ({row.EquippedIn}). Unequip and remove it?", "Remove item"))
        {
            return;
        }
        InventoryEditor.Remove(_session.Save, SelectedContainer.Container, row.Item);
        Items.Remove(row);
        Changed();
        RefreshView();
    }

    [RelayCommand]
    private void UpgradeAll(string type)
    {
        if (_session == null)
        {
            return;
        }
        var rows = Items.Where(r => r.HasTiers && (type == "All" || r.Type == type || (type == "Weapon" && r.Type == "WeaponItem"))).ToList();
        if (rows.Count == 0 || !UserPrompt.Confirm($"Set {rows.Count} items to Tier 5++?", "Upgrade all"))
        {
            return;
        }
        foreach (var row in rows)
        {
            InventoryEditor.SetTier(_session.Save, row.Item, ItemTier.Max);
            row.RefreshTier();
        }
        Changed();
    }

    [RelayCommand]
    private void AddItem()
    {
        if (SelectedContainer == null || _session == null)
        {
            return;
        }
        var addItem = new AddItemViewModel(IsFemale, SaveFlags.AdditionalContent(_session.Save).Count > 0);
        var dialog = new Views.AddItemDialog(addItem) { Owner = System.Windows.Application.Current.MainWindow };
        if (dialog.ShowDialog() != true || addItem.Result is not { Count: > 0 } choices)
        {
            return;
        }

        try
        {
            ItemData? last = null;
            foreach (var choice in choices)
            {
                last = InventoryEditor.Add(_session.Save, SelectedContainer.Container, choice.Item.Id, (uint)choice.Quantity);
                if (choice.Tier is { } tier && InventoryEditor.HasTiers(choice.Item.Type))
                {
                    InventoryEditor.SetTier(_session.Save, last, tier);
                }
            }
            Changed();
            LoadItems();
            SelectedItem = Items.FirstOrDefault(r => ReferenceEquals(r.Item, last));
        }
        catch (Exception e)
        {
            UserPrompt.Notice("Could not add the item: " + e.Message, "Add item");
        }
    }
}
