namespace HKSK.Cache;

/// <summary>
/// An event the clip announces, and when.
/// </summary>
/// <remarks>
/// Written as <c>name:time</c>. The name never contains a colon -- none of the
/// 36,584 events in the shipped game does -- so the separator is unambiguous,
/// though 3,941 names carry a dot, which is the payload separator the behaviour
/// uses (<c>SoundPlay.NPCChickenScratch</c>).
///
/// The time is in clip seconds, and the list merges two sources: the animation's
/// own annotation track and the behaviour's clip triggers. See
/// <c>HKSK.Fbx.AnimationExchange</c>.
/// </remarks>
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
/// <c>HKSK.Model.ActorProject</c>.
/// </remarks>
public sealed class ClipGeneratorEntry
{
    public string Name { get; set; } = "";

    /// <summary>Position of this clip's animation in the character's animation list.</summary>
    public int CacheIndex { get; set; }

    /// <summary>
    /// How fast the clip plays the animation, copied from the generator.
    /// </summary>
    /// <remarks>
    /// Verbatim: all 10,556 clips in the shipped game that have a generator
    /// agree with it exactly, so any difference is an edit that never reached
    /// the cache.
    /// </remarks>
    public float PlaybackSpeed { get; set; } = 1f;

    /// <summary>Seconds trimmed from the start, copied from the generator.</summary>
    public float CropStartTime { get; set; }

    /// <summary>Seconds trimmed from the end, copied from the generator.</summary>
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
