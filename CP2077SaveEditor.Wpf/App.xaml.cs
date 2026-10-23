using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CP2077SaveEditor.Wpf.ViewModels;

namespace CP2077SaveEditor.Wpf;

public partial class App : Application
{
    public static string LogPath { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CP2077 Save Editor", "error.log");

    public static void Log(Exception exception)
    {
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(LogPath)!);
            System.IO.File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // logging must never take the app down
        }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        Services.NativeLibraries.Register();

        // Headless mode used by GameDataService to build the game data index in a separate process.
        if (e.Args.Length == 2 && e.Args[0] is Services.GameDataService.BuildIndexSwitch or Services.GameDataService.BuildIconsSwitch)
        {
            Shutdown(Services.GameDataService.RunBuilder(e.Args[0], e.Args[1]));
            return;
        }

        base.OnStartup(e);
        UserPrompt.Current = new DialogUserPrompt();

        DispatcherUnhandledException += (_, args) =>
        {
            Log(args.Exception);
            Views.ThemedDialog.ShowNotice(MainWindow, $"Something went wrong:\n\n{args.Exception.GetBaseException().Message}\n\nDetails were written to {LogPath}.\n\nYour save files have not been modified.", "Error");
            args.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, args) => { Log(args.Exception); args.SetObserved(); };

        var viewModel = new MainViewModel();
        var window = new MainWindow { DataContext = viewModel };
        MainWindow = window;
        window.Show();

        // Show the window first, then fill it: scanning saves happens in the background.
        _ = viewModel.Browser.RefreshAsync();

        // Real item names and icons from the player's own game install (built once per game version).
        _ = Services.GameDataService.Instance.InitializeAsync();

        // WolvenKit's one-time scan of the game's type database takes a few seconds; do it while the user
        // browses saves so opening one is quick.
        _ = Task.Run(() =>
        {
            WolvenKit.RED4.Types.RedReflection.GetTypes();
            WolvenKit.RED4.Save.SaveHashHelper.GetTypeFromHash(0);
        });

        // A save folder or sav.dat passed on the command line (or dropped onto the exe) is opened directly;
        // "--page <name>" then switches to that screen.
        string? path = null, page = null;
        for (var i = 0; i < e.Args.Length; i++)
        {
            if (e.Args[i] == "--page" && i + 1 < e.Args.Length) page = e.Args[++i];
            else path ??= e.Args[i];
        }
        if (path != null)
        {
            _ = OpenAndShowAsync(viewModel, path, page);
        }
    }

    private static async Task OpenAndShowAsync(MainViewModel viewModel, string path, string? page)
    {
        await viewModel.OpenPathAsync(path);
        if (page != null && viewModel.Pages.FirstOrDefault(p => string.Equals(p.Title, page, StringComparison.OrdinalIgnoreCase)) is { IsEnabled: true } target)
        {
            viewModel.CurrentPage = target;
        }
    }
}

internal sealed class DialogUserPrompt : IUserPrompt
{
    public void Notice(string message, string title) =>
        Current.Dispatcher.Invoke(() => Views.ThemedDialog.ShowNotice(Current.MainWindow, message, title));

    public bool Confirm(string message, string title) =>
        Current.Dispatcher.Invoke(() => Views.ThemedDialog.ShowConfirm(Current.MainWindow, message, title));

    private static Application Current => Application.Current;
}
