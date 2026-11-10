using System.IO;

namespace MmaManager.Wpf;

/// <summary>
/// The default home for career saves (Documents\MMA Manager\Saves): the save and
/// load dialogs open here, and the start screen's CONTINUE button resumes the
/// most recently written save in it. Files remain plain *.mmasave — saving
/// elsewhere via the dialog still works, it just won't feed CONTINUE.
/// </summary>
public static class SaveLocator
{
    public static string Dir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MMA Manager", "Saves");

    /// <summary>The directory, created on first use.</summary>
    public static string Ensure()
    {
        Directory.CreateDirectory(Dir);
        return Dir;
    }

    /// <summary>The newest save in the default directory, or null.</summary>
    public static FileInfo? MostRecent() =>
        Directory.Exists(Dir)
            ? new DirectoryInfo(Dir).GetFiles("*.mmasave").MaxBy(f => f.LastWriteTimeUtc)
            : null;
}
