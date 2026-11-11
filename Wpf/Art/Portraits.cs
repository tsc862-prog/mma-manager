using System.Windows.Media;
using MmaManager.Core.Model;

namespace MmaManager.Wpf.Art;

/// <summary>
/// The portrait resolution used across every screen — kept in one place so the
/// art style is a one-line switch. Current default: the AI-painted pack
/// (<see cref="PaintedLibrary"/>, face + body per life stage), with the
/// flat-illustration <see cref="CharacterFactory"/> filling any gap the pack
/// can't cover. The bundled photo pool (<see cref="FaceLibrary"/>) and the
/// soft vector <see cref="PortraitFactory"/> remain available as alternatives.
/// </summary>
public static class Portraits
{
    public static ImageSource For(Fighter f, World w)
    {
        PaintedLibrary.AttachWorld(w);   // country culture -> better identity match
        var age = f.AgeOn(w.CurrentDate);
        // Anonymous local-scene filler shows the grey placeholder (by class+sex)
        // until they're discovered; then LocalTalent clears and the pack takes over.
        if (f.LocalTalent && PlaceholderLibrary.Face(f) is { } ph)
            return ph;
        return PaintedLibrary.Face(f, age) ?? CharacterFactory.Face(f, age);
    }

    /// <summary>Pixel width small faces are decoded at: crisp at 150% scaling on a 96 px card.</summary>
    public const int ThumbWidth = 160;

    // Small faces by file path (world-independent), the files being decoded right now
    // with whoever is waiting for them, and each fighter's face file for this world.
    private static readonly Dictionary<string, ImageSource> ThumbCache = new();
    private static readonly Dictionary<string, List<Action<ImageSource>>> InFlight = new();
    private static readonly Dictionary<(int id, int age), string?> PathCache = new();
    private static World? _pathWorld;

    /// <summary>Harness A/B switch (--syncfaces): decode every card face full-size on the UI thread, as before.</summary>
    public static bool SyncThumbs;

    /// <summary>How many small faces are still being decoded (the render harness waits for zero).</summary>
    public static int PendingThumbs => InFlight.Count;

    private static string? FacePath(Fighter f, World w, int age)
    {
        if (!ReferenceEquals(_pathWorld, w)) { PathCache.Clear(); _pathWorld = w; }
        if (!PathCache.TryGetValue((f.Id.Value, age), out var path))
            PathCache[(f.Id.Value, age)] = path = PaintedLibrary.FacePath(f, age);
        return path;
    }

    /// <summary>
    /// The same face as <see cref="For"/> for a card, row or avatar. Returns it now
    /// when it is ready (drawn art, a placeholder, or a face decoded before);
    /// otherwise returns null and decodes it at <see cref="ThumbWidth"/> px on a
    /// worker thread, calling <paramref name="ready"/> on the UI thread when done.
    /// A wall of 140 cards opens at once and its faces decode in parallel, instead
    /// of the window waiting on 140 full-size PNG decodes.
    /// </summary>
    public static ImageSource? Thumb(Fighter f, World w, Action<ImageSource> ready)
    {
        if (SyncThumbs) return For(f, w);
        PaintedLibrary.AttachWorld(w);
        var age = f.AgeOn(w.CurrentDate);
        if (f.LocalTalent && PlaceholderLibrary.Face(f) is { } ph)
            return ph;
        if (FacePath(f, w, age) is not { } path)
            return CharacterFactory.Face(f, age);
        if (ThumbCache.TryGetValue(path, out var done)) return done;
        if (InFlight.TryGetValue(path, out var waiting)) { waiting.Add(ready); return null; }

        InFlight[path] = new List<Action<ImageSource>> { ready };
        var ui = System.Windows.Threading.Dispatcher.CurrentDispatcher;
        Task.Run(() =>
        {
            ImageSource? img;
            try { img = PaintedLibrary.DecodeThumb(path, ThumbWidth); }
            catch { img = null; }
            Decoded.Enqueue((path, img, f, age));
            // One hop back to the UI thread hands over every face decoded so far, so a
            // wall of faces lands in a few layout passes rather than one per face.
            if (Interlocked.Exchange(ref _flushQueued, 1) == 0)
                ui.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(Flush));
        });
        return null;
    }

    private static readonly System.Collections.Concurrent.ConcurrentQueue<(string Path, ImageSource? Img, Fighter F, int Age)> Decoded = new();
    private static int _flushQueued;

    private static void Flush()
    {
        Interlocked.Exchange(ref _flushQueued, 0);
        while (Decoded.TryDequeue(out var d))
        {
            var img = d.Img ?? PaintedLibrary.Face(d.F, d.Age) ?? CharacterFactory.Face(d.F, d.Age);
            ThumbCache[d.Path] = img;
            if (InFlight.Remove(d.Path, out var callbacks))
                foreach (var cb in callbacks) cb(img);
        }
    }

    /// <summary>
    /// The face for a small spot, synchronously: the decoded thumbnail when there is one,
    /// otherwise the full portrait (decoding a full PNG on the UI thread is quicker than
    /// scaling it down there). For a handful of faces; walls use the callback overload.
    /// </summary>
    public static ImageSource Thumb(Fighter f, World w)
    {
        PaintedLibrary.AttachWorld(w);
        var age = f.AgeOn(w.CurrentDate);
        if (!(f.LocalTalent && PlaceholderLibrary.Face(f) is not null)
            && FacePath(f, w, age) is { } path && ThumbCache.TryGetValue(path, out var done))
            return done;
        return For(f, w);
    }

    /// <summary>Full-body shot of the same person as the face (Watch-tab intros etc.).</summary>
    public static ImageSource Body(Fighter f, World w)
    {
        PaintedLibrary.AttachWorld(w);
        var age = f.AgeOn(w.CurrentDate);
        if (f.LocalTalent && PlaceholderLibrary.Body(f) is { } ph)
            return ph;
        return PaintedLibrary.Body(f, age) ?? CharacterFactory.Body(f, age);
    }
}
