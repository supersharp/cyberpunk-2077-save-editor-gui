using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace CP2077SaveEditor.Wpf.Views;

public partial class ThemedDialog : Window
{
    public string Message { get; }
    public string TitleUpper => (Title ?? "NOTICE").ToUpperInvariant();
    public bool IsConfirm { get; }
    public string Glyph { get; }
    public Brush HeaderBrush { get; }
    public Brush GlyphBrush { get; }

    public ThemedDialog(string title, string message, bool isConfirm)
    {
        Title = string.IsNullOrWhiteSpace(title) ? (isConfirm ? "Confirm" : "Notice") : title;
        Message = message;
        IsConfirm = isConfirm;

        var lower = Title.ToLowerInvariant();
        if (lower.Contains("error") || lower.Contains("failed") || lower.Contains("invalid") || lower.Contains("unsupported"))
        {
            Glyph = "\uE783"; // Error badge
            HeaderBrush = (Brush)Application.Current.FindResource("RedBrush");
            GlyphBrush = (Brush)Application.Current.FindResource("TextBrush");
        }
        else if (isConfirm || lower.Contains("warning") || lower.Contains("discard") || lower.Contains("remove") || lower.Contains("lock"))
        {
            Glyph = "\uE7BA"; // Warning triangle
            HeaderBrush = (Brush)Application.Current.FindResource("AccentBrush");
            GlyphBrush = (Brush)Application.Current.FindResource("BgBrush");
        }
        else
        {
            Glyph = "\uE946"; // Info circle
            HeaderBrush = (Brush)Application.Current.FindResource("CyanBrush");
            GlyphBrush = (Brush)Application.Current.FindResource("BgBrush");
        }

        DataContext = this;
        InitializeComponent();
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    public static void ShowNotice(Window? owner, string message, string title = "Notice")
    {
        var dlg = new ThemedDialog(title, message, isConfirm: false);
        ConfigureOwner(dlg, owner);
        dlg.ShowDialog();
    }

    public static bool ShowConfirm(Window? owner, string message, string title = "Confirm")
    {
        var dlg = new ThemedDialog(title, message, isConfirm: true);
        ConfigureOwner(dlg, owner);
        return dlg.ShowDialog() == true;
    }

    private static void ConfigureOwner(Window dlg, Window? owner)
    {
        owner ??= Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w.IsVisible)
                  ?? Application.Current?.MainWindow;

        if (owner != null && owner.IsVisible)
        {
            dlg.Owner = owner;
            dlg.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }
}
