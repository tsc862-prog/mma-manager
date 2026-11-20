namespace MmaManager.Tests;

/// <summary>
/// No RadioButton in the app may carry a GroupName. A named group is app-wide in WPF, so two copies of
/// one screen (an open window and a closed one not yet collected) shared a group and ping-ponged through
/// their bindings until the stack overflowed (the Card Desk, 2026-11-20). Segmented options group by
/// their panel instead (SegmentedOption in Wpf/App.xaml). This reads the XAML as text, so it needs no
/// WPF reference and runs on any platform; `MmaManager.Wpf --radiocheck` is the live check.
/// </summary>
public class NoNamedRadioGroupsTests
{
    [Fact]
    public void No_xaml_file_in_the_app_names_a_radio_group()
    {
        var wpf = Path.Combine(RepoRoot(), "Wpf");
        Assert.True(Directory.Exists(wpf), $"no Wpf folder under the repo root ({wpf})");

        var sep = Path.DirectorySeparatorChar;
        var files = Directory.EnumerateFiles(wpf, "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{sep}bin{sep}") && !f.Contains($"{sep}obj{sep}"))
            .ToList();
        Assert.NotEmpty(files);

        var hits = new List<string>();
        foreach (var file in files)
        {
            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
                if (lines[i].Contains("GroupName=", StringComparison.Ordinal))
                    hits.Add($"{Path.GetRelativePath(wpf, file)}:{i + 1}: {lines[i].Trim()}");
        }
        Assert.True(hits.Count == 0,
            "RadioButton GroupName is app-wide and must not be used (group options by their panel instead):\n"
            + string.Join("\n", hits));
    }

    /// <summary>Walk up from the test's base directory to the folder holding MmaManager.sln.</summary>
    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "MmaManager.sln")))
                return dir.FullName;
        throw new InvalidOperationException($"MmaManager.sln not found above {AppContext.BaseDirectory}");
    }
}
