namespace HKSK.Cache;

/// <summary>An event the clip announces, and when.</summary>
public sealed record ClipEvent(string Name, float Time);

/// <summary>
/// One clip generator as the cache records it.
/// </summary>
/// <remarks>
/// This is a flattened copy of an <c>hkbClipGenerator</c> from the behaviour
/// graph: the same name, playback speed, crop times and trigger list, restated
/// so the game can read them without loading the behaviour files.
///
/// <see cref="CacheIndex"/> is the part that is not a copy. It is the position
/// of this clip's animation in the character file's animation list, and it is
/// what ties the clip to its root motion -- see
/// <c>HKSK.Model.HavokProject</c>.
/// </remarks>
public sealed class ClipGeneratorEntry
{
    public string Name { get; set; } = "";

    /// <summary>Position of this clip's animation in the character's animation list.</summary>
    public int CacheIndex { get; set; }

    public float PlaybackSpeed { get; set; } = 1f;
    public float CropStartTime { get; set; }
    public float CropEndTime { get; set; }

    public List<ClipEvent> Events { get; set; } = [];

    public static ClipGeneratorEntry Read(LineCursor c)
    {
        var clip = new ClipGeneratorEntry
        {
            Name = c.Line("clip name"),
            CacheIndex = c.Int("clip cache index"),
            PlaybackSpeed = c.Float("playback speed"),
            CropStartTime = c.Float("crop start time"),
            CropEndTime = c.Float("crop end time"),
        };

        int count = c.Int("clip event count");
        for (int i = 0; i < count; i++)
        {
            string line = c.Line("clip event");

            // Event names are free text and the time is appended after a colon,
            // so the last colon is the separator, not the first.
            int split = line.LastIndexOf(':');
            if (split < 0) throw c.Fail("an event as 'name:time'", line);

            clip.Events.Add(new ClipEvent(line[..split], CacheText.ParseFloat(line[(split + 1)..])));
        }

        c.SkipBlank();
        return clip;
    }

    public void Write(LineWriter w)
    {
        w.Line(Name);
        w.Int(CacheIndex);
        w.Float(PlaybackSpeed);
        w.Float(CropStartTime);
        w.Float(CropEndTime);
        w.Counted(Events, static (lw, e) => lw.Line($"{e.Name}:{CacheText.Float(e.Time)}"));
        w.Blank();
    }

    public ClipGeneratorEntry Clone() => new()
    {
        Name = Name,
        CacheIndex = CacheIndex,
        PlaybackSpeed = PlaybackSpeed,
        CropStartTime = CropStartTime,
        CropEndTime = CropEndTime,
        Events = [.. Events],
    };
}
