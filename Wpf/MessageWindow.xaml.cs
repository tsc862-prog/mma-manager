using System.Windows;
using System.Windows.Media;

namespace MmaManager.Wpf;

/// <summary>
/// A small blocking message in the app's dialog style, for the few results the
/// player must not miss (game over, a failed save, a failed load). Routine
/// results go to the shell's result strip instead. Given a confirm label it
/// becomes a two-button question before something destructive; the safe
/// "keep" button is then the default (Enter and Esc both choose it).
/// </summary>
public partial class MessageWindow : Window
{
    public MessageWindow(string heading, string body, string? detail = null, bool severe = false,
                         string? confirm = null, string keep = "Keep it")
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
        if (confirm is not null)
        {
            KeepButton.Content = keep;
            KeepButton.Visibility = Visibility.Visible;
            KeepButton.IsDefault = true;
            KeepButton.IsCancel = true;
            OkButton.IsDefault = false;
            OkButton.IsCancel = false;
            OkButton.Content = confirm;
            if (TryFindResource("Negative") is Brush danger)
            {
                OkButton.Foreground = danger;
                OkButton.BorderBrush = danger;
            }
        }
    }

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = true;
    private void OnKeep(object sender, RoutedEventArgs e) => DialogResult = false;

    /// <summary>Show the message over the main window and wait for OK.</summary>
    public static void ShowOver(string heading, string body, string? detail = null, bool severe = false) =>
        ShowModal(new MessageWindow(heading, body, detail, severe));

    /// <summary>Ask before something destructive; true only if the player chose <paramref name="confirm"/>.</summary>
    public static bool Ask(string heading, string body, string? detail, string confirm, string keep) =>
        ShowModal(new MessageWindow(heading, body, detail, severe: true, confirm, keep)) == true;

    private static bool? ShowModal(MessageWindow win)
    {
        if (Application.Current?.MainWindow is { IsLoaded: true } owner && !ReferenceEquals(owner, win))
            win.Owner = owner;
        else
            win.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return win.ShowDialog();
    }
}
