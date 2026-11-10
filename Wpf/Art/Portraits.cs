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
