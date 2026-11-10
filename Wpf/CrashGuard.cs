using System.IO;
using System.Windows;
using System.Windows.Threading;
using MmaManager.Persistence;
using MmaManager.Wpf.ViewModels;

namespace MmaManager.Wpf;

/// <summary>
/// The last line of defence for the interactive app: an exception that escapes a
/// view or view model on the UI thread is logged, the live world is written to a
/// separate rescue save (never the autosave), and the player is told what
/// happened and where both files are. The game keeps running when it sensibly
/// can; otherwise it closes cleanly.
///
/// Installed only for an interactive launch. Headless harness modes never call
/// <see cref="Install"/>, so they keep failing loudly on the console.
/// </summary>
public static class CrashGuard
{
    /// <summary>Machine-local app data (the same folder as display.json and desktop-layout.json).</summary>
    public static string Dir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MmaManager");

    public static string LogPath { get; } = Path.Combine(Dir, "crash.log");

    // Crash-storm guard: an error that fires on every layout pass would otherwise
    // put up a dialog forever.
    private const int StormLimit = 3;
    private static readonly TimeSpan StormWindow = TimeSpan.FromSeconds(30);
    private static readonly Queue<DateTime> Recent = new();
    private static bool _handling;

    public static void Install(Application app) => app.DispatcherUnhandledException += OnUnhandled;

    private static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var ex = e.Exception;
        var now = DateTime.Now;

        // A second failure while the first dialog is still up: log it and let the
        // first dialog decide. Never stack dialogs.
        if (_handling)
        {
            WriteLog(ex, "(while handling an earlier error)");
            Note(now);
            e.Handled = true;
            return;
        }

        _handling = true;
        try
        {
            WriteLog(ex, null);
            Note(now);
            string? rescue = TryRescueSave();

            var owner = Application.Current?.MainWindow;
            bool windowUp = owner is { IsLoaded: true };
            bool canContinue = windowUp && !IsFatal(ex) && Recent.Count < StormLimit;

            string what = ex is System.Windows.Markup.XamlParseException
                ? "A screen failed to open."
                : "Something went wrong inside the game.";
            string saved = rescue is null
                ? "The game could not write a rescue save. Your last autosave is untouched."
                : $"Your career was saved to a separate rescue file (your autosave is untouched):\n{rescue}\n\nOpen it with Load Career if you need it.";
            string body =
                $"{what}\n\n{FirstLine(ex)}\n\n{saved}\n\nThe details are in the log:\n{LogPath}";

            if (canContinue)
            {
                body += "\n\nKeep playing? Choose No to close the game.";
                var answer = Show(owner, body, MessageBoxButton.YesNo, MessageBoxImage.Warning);
                // A storm that built up while the dialog was open also ends the session.
                if (answer == MessageBoxResult.Yes && Recent.Count < StormLimit)
                {
                    e.Handled = true;
                    return;
                }
            }
            else
            {
                body += "\n\nThe game has to close.";
                Show(windowUp ? owner : null, body, MessageBoxButton.OK, MessageBoxImage.Error);
            }

            e.Handled = true;
            ExitCleanly();
        }
        catch (Exception guardFailure)
        {
            // The guard itself must never be the thing that crashes the app badly.
            WriteLog(guardFailure, "(the crash handler failed)");
            e.Handled = true;
            ExitCleanly();
        }
        finally
        {
            _handling = false;
        }
    }

    private static void Note(DateTime now)
    {
        Recent.Enqueue(now);
        while (Recent.Count > 0 && now - Recent.Peek() > StormWindow) Recent.Dequeue();
    }

    private static bool IsFatal(Exception ex) =>
        ex is OutOfMemoryException or InsufficientExecutionStackException
            or AccessViolationException or InvalidProgramException or BadImageFormatException;

    private static string FirstLine(Exception ex)
    {
        // A XAML failure's real cause is usually the inner exception's message.
        string msg = ex.Message;
        if (ex is System.Windows.Markup.XamlParseException && ex.InnerException is { } inner)
            msg = inner.Message;
        int nl = msg.IndexOfAny(['\r', '\n']);
        return nl > 0 ? msg[..nl] : msg;
    }

    /// <summary>Saves the live world to Rescue\rescue-*.mmasave. Returns the path, or
    /// null if there was no world or the save failed. Never throws.</summary>
    private static string? TryRescueSave()
    {
        try
        {
            if (Application.Current?.MainWindow?.DataContext is not MainViewModel vm
                || vm.LiveWorld is not { } world)
                return null;
            string dir = Path.Combine(Dir, "Rescue");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, $"rescue-{DateTime.Now:yyyyMMdd-HHmmss}.mmasave");
            SaveGame.Save(world, path);
            return path;
        }
        catch (Exception saveFailure)
        {
            WriteLog(saveFailure, "(rescue save failed)");
            return null;
        }
    }

    private static void WriteLog(Exception ex, string? note)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.AppendAllText(LogPath,
                $"==== {DateTime.Now:yyyy-MM-dd HH:mm:ss} {note}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { /* nowhere left to report to */ }
    }

    private static MessageBoxResult Show(Window? owner, string body, MessageBoxButton buttons, MessageBoxImage icon)
    {
        try
        {
            return owner is not null
                ? MessageBox.Show(owner, body, "MMA Manager", buttons, icon)
                : MessageBox.Show(body, "MMA Manager", buttons, icon);
        }
        catch
        {
            return MessageBoxResult.No;
        }
    }

    private static void ExitCleanly()
    {
        try { Application.Current?.Shutdown(1); }
        catch { Environment.Exit(1); }
    }
}
