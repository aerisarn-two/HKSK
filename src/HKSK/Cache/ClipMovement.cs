using System.Numerics;

namespace HKSK.Cache;

/// <summary>Where the root is at a given time.</summary>
public sealed record TranslationKey(float Time, Vector3 Value);

/// <summary>How the root is turned at a given time.</summary>
public sealed record RotationKey(float Time, Quaternion Value);

/// <summary>
/// The root motion of one animation, as the cache records it.
/// </summary>
/// <remarks>
/// The game needs to know how far an animation travels before it plays it --
/// to place the actor, to pick between a walk and a run, to work out whether a
/// turn will fit. Loading the animation to find out would defeat the point, so
/// the motion is extracted from the animation's
/// <c>hkaAnimatedReferenceFrame</c> and restated here.
///
/// Keyed by <see cref="CacheIndex"/>, which is a position in the character's
/// animation list -- so root motion belongs to an <em>animation</em>, and every
/// clip playing that animation shares it.
/// </remarks>
public sealed class ClipMovement
{
    public int CacheIndex { get; set; }
    public float Duration { get; set; }
    public List<TranslationKey> Translations { get; set; } = [];
    public List<RotationKey> Rotations { get; set; } = [];

    /// <summary>Whether the root goes anywhere at all.</summary>
    public bool HasMovement =>
        Translations.Any(t => t.Value != Vector3.Zero) ||
        Rotations.Any(r => r.Value != Quaternion.Identity);

    /// <summary>How far the root travels from first key to last.</summary>
    public float Travel =>
        Translations.Count < 2
            ? 0f
            : Vector3.Distance(Translations[0].Value, Translations[^1].Value);

    public static ClipMovement Read(LineCursor c)
    {
        var m = new ClipMovement
        {
            CacheIndex = c.Int("movement cache index"),
            Duration = c.Float("movement duration"),
        };

        int count = c.Int("translation count");
        for (int i = 0; i < count; i++)
        {
            string[] p = Fields(c, "a translation as 'time x y z'", 4);
            m.Translations.Add(new TranslationKey(
                CacheText.ParseFloat(p[0]),
                new Vector3(CacheText.ParseFloat(p[1]), CacheText.ParseFloat(p[2]), CacheText.ParseFloat(p[3]))));
        }

        count = c.Int("rotation count");
        for (int i = 0; i < count; i++)
        {
            string[] p = Fields(c, "a rotation as 'time x y z w'", 5);
            m.Rotations.Add(new RotationKey(
                CacheText.ParseFloat(p[0]),
                new Quaternion(CacheText.ParseFloat(p[1]), CacheText.ParseFloat(p[2]),
                               CacheText.ParseFloat(p[3]), CacheText.ParseFloat(p[4]))));
        }

        c.SkipBlank();
        return m;
    }

    private static string[] Fields(LineCursor c, string what, int expected)
    {
        string line = c.Line(what);
        string[] parts = line.Split(' ');
        return parts.Length == expected ? parts : throw c.Fail(what, line);
    }

    public void Write(LineWriter w)
    {
        w.Int(CacheIndex);
        w.Float(Duration);
        w.Counted(Translations, static (lw, t) => lw.Line(string.Join(' ',
            CacheText.Float(t.Time), CacheText.Float(t.Value.X),
            CacheText.Float(t.Value.Y), CacheText.Float(t.Value.Z))));
        w.Counted(Rotations, static (lw, r) => lw.Line(string.Join(' ',
            CacheText.Float(r.Time), CacheText.Float(r.Value.X), CacheText.Float(r.Value.Y),
            CacheText.Float(r.Value.Z), CacheText.Float(r.Value.W))));
        w.Blank();
    }

    public ClipMovement Clone() => new()
    {
        CacheIndex = CacheIndex,
        Duration = Duration,
        Translations = [.. Translations],
        Rotations = [.. Rotations],
    };
}
