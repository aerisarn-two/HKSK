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

    /// <summary>
    /// How far the root ends up from where it started.
    /// </summary>
    /// <remarks>
    /// The curve is a displacement from the start of the animation, and it is
    /// implicitly zero at time zero: no block in the shipped game carries a key
    /// at t=0, and 5,769 of the 6,725 carry a single key at the end holding the
    /// whole displacement. So this is the magnitude of the last value, not the
    /// distance between the first key and the last -- which would be zero for
    /// most of the game.
    /// </remarks>
    public float Travel => Translations.Count == 0 ? 0f : Translations[^1].Value.Length();

    /// <summary>How far the root turns over the animation, in radians.</summary>
    public float Turn
    {
        get
        {
            if (Rotations.Count == 0) return 0f;

            Quaternion last = Rotations[^1].Value;
            return 2f * MathF.Acos(Math.Clamp(MathF.Abs(last.W), -1f, 1f));
        }
    }

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
