namespace Shimmer.Models;

/// <summary>
/// A single icon on the real Windows desktop, identified by its position in the
/// desktop ListView plus its label. We key membership off <see cref="Label"/>
/// because the ListView index is not stable across explorer restarts, whereas an
/// icon's caption (e.g. "Recycle Bin", "project.docx") is a good-enough identity.
/// </summary>
public sealed record DesktopIcon(int Index, string Label, int X, int Y)
{
    public override string ToString() => $"[{Index}] \"{Label}\" @ ({X},{Y})";
}
