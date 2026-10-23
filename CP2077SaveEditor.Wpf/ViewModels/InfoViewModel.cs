using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CP2077SaveEditor.Wpf.Services;

namespace CP2077SaveEditor.Wpf.ViewModels;

/// <summary>A project this editor is built on, shown on the Info page.</summary>
public sealed record CreditEntry(string Name, string Author, string Role, string Url, string Glyph);

/// <summary>About the editor: version, download links, and the projects it builds on.</summary>
public sealed partial class InfoViewModel : PageViewModel
{
    public InfoViewModel() => IsEnabled = true;

    public override string Title => "Info";
    public override string Glyph => "";
    public override bool RequiresSave => false;

    public string Version => MainViewModel.Version;
    public string ForkedRepositoryUrl => UpdateService.ForkedRepositoryUrl;
    public string UpstreamRepositoryUrl => UpdateService.UpstreamRepositoryUrl;
    public string ForkedReleasesUrl => UpdateService.ForkedReleasesUrl;

    public IReadOnlyList<CreditEntry> Credits { get; } = new[]
    {
        new CreditEntry("CP2077 Save Editor (Fork)", "supersharp", "This fork: fuzzy search across all views, .NET 10 x64 modernization, and multi-branch release workflows.",
            UpdateService.ForkedRepositoryUrl, ""),
        new CreditEntry("CP2077 Save Editor (Upstream)", "AtfastrSlushyMaker", "Original patch 2.5 save support, modern WPF UI, vehicle pictures, catalog index and save library.",
            UpdateService.UpstreamRepositoryUrl, ""),
        new CreditEntry("CyberCAT-SimpleGUI", "Deweh", "The original save editor (Nexus mod 718) this project is based on. All original features and research are theirs.",
            "https://github.com/Deweh/CyberCAT-SimpleGUI", ""),
        new CreditEntry("WolvenKit", "The WolvenKit team", "Reads and writes the save, the game database and the game archives (names and icons).",
            "https://github.com/WolvenKit/WolvenKit", ""),
        new CreditEntry("CyberCAT", "SirBitesalot and contributors", "The original Cyberpunk 2077 save format research.",
            "https://github.com/WolvenKit/CyberCAT", ""),
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdate))]
    private UpdateInfo? _update;

    [ObservableProperty]
    private string _updateStatus = "Check for newer builds and releases on the forked repository.";

    [ObservableProperty]
    private bool _isBusy;

    public bool HasUpdate => Update != null;

    [RelayCommand]
    private void OpenLink(string url) => UpdateService.OpenUrl(url);

    [RelayCommand]
    public async Task CheckForUpdatesAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        UpdateStatus = "Checking supersharp/cyberpunk-2077-save-editor-gui for new builds…";
        try
        {
            Update = await UpdateService.CheckAsync();
            if (Update != null)
            {
                UpdateStatus = $"A new build ({Update.Tag}) is available on the forked repository!";
            }
            else
            {
                UpdateStatus = $"You are on the latest build ({Version}). No newer releases found on the forked repository.";
            }
        }
        catch (Exception ex)
        {
            UpdateStatus = "Could not check for updates: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenReleases() => UpdateService.OpenUrl(Update?.PageUrl ?? UpdateService.ForkedReleasesUrl);
}
