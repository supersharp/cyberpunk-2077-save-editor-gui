using System;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace CP2077SaveEditor.Wpf.Services;

/// <summary>A newer release on GitHub.</summary>
public sealed record UpdateInfo(Version Version, string Tag, string PageUrl, string? DownloadUrl, string Notes);

/// <summary>
/// Checks the GitHub repository (supersharp/cyberpunk-2077-save-editor-gui) for releases and new builds.
/// </summary>
public static class UpdateService
{
    public const string Repository = "supersharp/cyberpunk-2077-save-editor-gui";
    public const string UpstreamRepository = "AtfastrSlushyMaker/cyberpunk-2077-save-editor-gui";

    public static string RepositoryUrl => $"https://github.com/{Repository}";
    public static string ReleasesUrl => $"{RepositoryUrl}/releases";
    public static string LatestReleaseUrl => $"{ReleasesUrl}/latest";
    public static string UpstreamRepositoryUrl => $"https://github.com/{UpstreamRepository}";

    public static Version CurrentVersion { get; } = typeof(UpdateService).Assembly.GetName().Version ?? new Version(1, 1, 3);

    private static readonly HttpClient s_http = CreateClient();

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("CP2077SaveEditor/" + CurrentVersion.ToString(3));
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    }

    /// <summary>The latest release on the repository if it is newer than this build, otherwise null.</summary>
    public static async Task<UpdateInfo?> CheckAsync()
    {
        using var response = await s_http.GetAsync($"https://api.github.com/repos/{Repository}/releases/latest");
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null; // No releases published on repository yet
        }
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version) || Normalize(version) <= Normalize(CurrentVersion))
        {
            return null;
        }

        var downloadUrl = root.TryGetProperty("assets", out var assets)
            ? assets.EnumerateArray()
                .Select(a => (Name: a.GetProperty("name").GetString() ?? "", Url: a.GetProperty("browser_download_url").GetString()))
                .FirstOrDefault(a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)).Url
            : null;

        return new UpdateInfo(version, tag, root.GetProperty("html_url").GetString() ?? ReleasesUrl, downloadUrl,
            root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "");
    }

    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));

    public static void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
