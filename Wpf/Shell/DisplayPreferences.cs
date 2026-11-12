using System.IO;
using System.Text.Json;
using System.Windows;
using MmaManager.Wpf.Mvvm;
using MmaManager.Wpf.ViewModels;

namespace MmaManager.Wpf.Shell;

/// <summary>Presentation settings are machine-local and never change a career save.</summary>
public sealed class DisplayPreferences : ObservableObject
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MmaManager", "display.json");
    public static DisplayPreferences Current { get; } = new();
    public int[] TextSizes { get; } = [100, 115, 130];
    public string[] Densities { get; } = ["Comfortable", "Compact"];
    private int _textSize = 100;
    private string _density = "Comfortable";
    public int TextSize
    {
        get => _textSize;
        set { if (TextSizes.Contains(value) && Set(ref _textSize, value)) { Apply(); Save(); } }
    }
    public string Density
    {
        get => _density;
        set { if (Densities.Contains(value) && Set(ref _density, value)) { Apply(); Save(); } }
    }
    private bool _quietBriefing = true;
    /// <summary>Open the briefing even when the clock stopped with nothing needing you (else a toast offers it).</summary>
    public bool BriefingOnQuietWeeks
    {
        get => _quietBriefing;
        set { if (Set(ref _quietBriefing, value)) Save(); }
    }
    public void Initialize()
    {
        if (!MainViewModel.HeadlessAutoBoot)
            try
            {
                if (File.Exists(SettingsPath) && JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsPath)) is { } s)
                {
                    if (TextSizes.Contains(s.TextSize)) _textSize = s.TextSize;
                    if (Densities.Contains(s.Density)) _density = s.Density;
                    _quietBriefing = s.QuietBriefing ?? true;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        Apply();
    }
    private void Apply()
    {
        if (Application.Current is not { } app) return;
        double scale = TextSize / 100.0;
        // Explicit XAML sizes also use these resources, so headings, tables and
        // supporting text all respond without scaling portraits or windows.
        double[] sizes = [9, 10, 10.5, 11, 11.5, 12, 12.5, 13, 13.5, 14, 15, 16, 17, 18, 19, 20, 21, 22, 24, 26, 28, 30, 32, 34, 36, 38, 40, 42, 44, 48, 52, 56, 60, 64, 72];
        foreach (double size in sizes)
            app.Resources["Font" + size.ToString(System.Globalization.CultureInfo.InvariantCulture).Replace('.', '_')] = size * scale;
        app.Resources["BodyFontSize"] = 13.0 * scale;
        app.Resources["TableRowHeight"] = (Density == "Compact" ? 25.0 : 33.0) * scale;
        app.Resources["FightRowHeight"] = (Density == "Compact" ? 54.0 : 68.0) * scale;
        app.Resources["ItemPadding"] = new Thickness(6, Density == "Compact" ? 3 : 7, 6, Density == "Compact" ? 3 : 7);
    }
    private sealed record Settings(int TextSize, string Density, bool? QuietBriefing = null);
    private void Save()
    {
        if (MainViewModel.HeadlessAutoBoot) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new Settings(TextSize, Density, BriefingOnQuietWeeks)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
