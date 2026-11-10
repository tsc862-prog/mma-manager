using System.Windows;
using System.Windows.Media;

namespace MmaManager.Wpf;

/// <summary>
/// A small blocking message in the app's dialog style, for the few results the
/// player must not miss (game over, a failed save, a failed load). Routine
/// results go to the shell's result strip instead.
/// </summary>
public partial class MessageWindow : Window
{
    public MessageWindow(string heading, string body, string? detail = null, bool severe = false)
    {
        InitializeComponent();
        Title = heading;
        HeadingText.Text = heading;
        BodyText.Text = body;
        if (!string.IsNullOrWhiteSpace(detail))
        {
            DetailText.Text = detail;
            DetailText.Visibility = Visibility.Visible;
        }
        if (severe && TryFindResource("Negative") is Brush red)
        {
            HeadingText.Foreground = red;
            Root.BorderBrush = red;
        }
    }

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = true;

    /// <summary>Show the message over the main window and wait for OK.</summary>
    public static void ShowOver(string heading, string body, string? detail = null, bool severe = false)
    {
        var win = new MessageWindow(heading, body, detail, severe);
        if (Application.Current?.MainWindow is { IsLoaded: true } owner && !ReferenceEquals(owner, win))
            win.Owner = owner;
        else
            win.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        win.ShowDialog();
    }
}
