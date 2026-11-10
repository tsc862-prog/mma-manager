using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace MmaManager.Wpf;

/// <summary>
/// The load-career slot picker: every save in the default directory
/// (<see cref="SaveLocator.Dir"/>), newest first, plus a Browse… fallback for
/// saves living elsewhere on disk. <see cref="ChosenPath"/> holds the pick.
/// </summary>
public partial class LoadGameWindow : Window
{
    public sealed record SlotRow(FileInfo File)
    {
        public string Name => Path.GetFileNameWithoutExtension(File.Name);
        public string Detail => $"saved {File.LastWriteTime:d MMM yyyy, HH:mm}  ·  {File.Length / 1024:N0} KB";
        public string Age
        {
            get
            {
                var span = DateTime.Now - File.LastWriteTime;
                return span.TotalHours < 1 ? $"{Math.Max(1, (int)span.TotalMinutes)} min ago"
                     : span.TotalDays < 1 ? $"{(int)span.TotalHours} h ago"
                     : $"{(int)span.TotalDays} d ago";
            }
        }
    }

    /// <summary>The save to load; null if cancelled.</summary>
    public string? ChosenPath { get; private set; }

    public LoadGameWindow()
    {
        InitializeComponent();

        var slots = Directory.Exists(SaveLocator.Dir)
            ? new DirectoryInfo(SaveLocator.Dir).GetFiles("*.mmasave")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Select(f => new SlotRow(f))
                .ToList()
            : new List<SlotRow>();

        foreach (var slot in slots) SlotBox.Items.Add(slot);
        SlotBox.SelectedIndex = slots.Count > 0 ? 0 : -1;
        if (slots.Count == 0)
        {
            EmptyText.Visibility = Visibility.Visible;
            LoadButton.IsEnabled = false;
        }
    }

    private void OnLoadClicked(object sender, RoutedEventArgs e)
    {
        if (SlotBox.SelectedItem is not SlotRow slot) return;
        ChosenPath = slot.File.FullName;
        DialogResult = true;
    }

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "MMA save (*.mmasave)|*.mmasave", DefaultExt = ".mmasave",
            InitialDirectory = SaveLocator.Ensure(),
        };
        if (dialog.ShowDialog(this) != true) return;
        ChosenPath = dialog.FileName;
        DialogResult = true;
    }
}
