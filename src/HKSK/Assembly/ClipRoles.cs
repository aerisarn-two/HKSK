namespace HKSK.Assembly;

/// <summary>A clip's role, its measured travel, and whether the two agree.</summary>
/// <param name="Clip">The animation as it was written.</param>
/// <param name="Animation">The animation and its roles, ready for a <see cref="CreatureSpec"/>.</param>
/// <param name="Why">Why it was read that way, which is the name that was read.</param>
/// <param name="Disagreement">
/// What the name says against what the feet say, where they differ, or null where they agree
/// or where there was nothing to compare.
/// </param>
public sealed record RoledClip(
    WrittenClip Clip,
    RoledAnimation Animation,
    string Why,
    string? Disagreement)
{
    public override string ToString() =>
        $"{Animation}" +
        (Clip.Travel is null ? "" : $" at {Clip.Travel.Speed:0.#} units a second") +
        (Disagreement is null ? "" : $" -- {Disagreement}");
}

/// <summary>What reading a set of clips produced.</summary>
/// <param name="Clips">One per animation, in the order they were written.</param>
/// <param name="Durations">How long each runs for, by its stem, which the assembler needs.</param>
/// <param name="Disagreements">The clips whose name and whose feet do not tell the same story.</param>
/// <param name="Unmeasured">
/// The locomotion clips left with no speed, and why. A clip that should carry the creature
/// and has no travel written down will not move it, so this is the list of rows somebody has
/// to fill in by hand.
/// </param>
public sealed record RoledClips(
    IReadOnlyList<RoledClip> Clips,
    IReadOnlyDictionary<string, float> Durations,
    IReadOnlyList<string> Disagreements,
    IReadOnlyList<string> Unmeasured)
{
    /// <summary>The animations, in the order they are to be numbered.</summary>
    public IReadOnlyList<RoledAnimation> Animations => [.. Clips.Select(c => c.Animation)];

    public override string ToString() =>
        $"{Clips.Count} clips read" +
        (Disagreements.Count == 0 ? "" : $", {Disagreements.Count} disagreeing with their feet") +
        (Unmeasured.Count == 0 ? "" : $", {Unmeasured.Count} travelling by an amount nobody has said");
}

/// <summary>
/// Reads what a set of converted clips are, and checks the reading against them.
/// </summary>
/// <remarks>
/// <para>
/// Two things say what a clip is and they are independent of each other. Its name says what
/// its author meant -- <c>WalkLeft</c>, <c>SwingHeavy</c>, <c>Death</c> -- and its feet say
/// what it actually does. Neither is reliable on its own: a name is a label an artist typed
/// and may be wrong or merely unhelpful, and the travel read off the feet is a measurement
/// with a confidence, which on a clip with little on the ground is low.
/// </para>
/// <para>
/// Together they are worth more than either. A clip called a walk whose feet say it goes
/// nowhere, or one called a left strafe whose feet carry it forwards, is one to look at
/// before it reaches the game -- and a clip whose name and feet agree can have the measured
/// speed written into its cache row with some confidence, which is the whole point of
/// measuring.
/// </para>
/// <para>
/// So neither is believed over the other. The name decides the role, because that is a thing
/// no measurement can supply -- nothing in a clip's feet says it is a death rather than a
/// lying down -- and the feet supply the speed, because that is a thing no name can supply.
/// Where the two disagree both are kept and the disagreement is reported, rather than one
/// being quietly dropped.
/// </para>
/// </remarks>
public static class ClipRoles
{
    /// <summary>How far a measured heading may be from its role's before they are said to differ.</summary>
    /// <remarks>
    /// Generous, because a heading read off feet is not a precise thing: a foot planted at a
    /// lateral offset sweeps a little as it passes, and a clip with a flight phase has fewer
    /// frames to average. Thirty-five degrees still separates the eight headings, which are
    /// forty-five apart.
    /// </remarks>
    public const float Tolerance = 35f;

    /// <summary>Below this the travel is reported but not written into a cache row.</summary>
    /// <remarks>
    /// A creature that walks has a foot down at every instant and reads at full confidence.
    /// One that runs has a flight phase and reads a little under. A jump has almost nothing
    /// down, and what it reads is not a speed the creature means to travel at.
    /// </remarks>
    public const float Believable = 0.5f;

    /// <summary>Reads every clip, and checks the reading against what it measured.</summary>
    /// <param name="written">The clips as the conversion produced them.</param>
    /// <param name="speeds">
    /// Whether to write the measured speed into the animations. False reads the roles and
    /// reports the travel without acting on it, which is what to do while still looking.
    /// </param>
    public static RoledClips Of(WrittenClips written, bool speeds = true)
    {
        ArgumentNullException.ThrowIfNull(written);

        var clips = new List<RoledClip>(written.Clips.Count);
        var durations = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        var disagreements = new List<string>();
        var unmeasured = new List<string>();

        foreach (WrittenClip clip in written.Clips)
        {
            ReadRole read = RoleReader.Of(clip.Name);
            string? quarrel = Quarrel(read, clip);

            if (quarrel is not null) disagreements.Add($"{clip.Name}: {quarrel}");

            // A speed is written only where the name and the feet tell the same story and the
            // measurement was worth having. Everything else keeps its travel unstated, which
            // a cache row is entitled to be.
            float? speed = speeds && quarrel is null && Moves(read.Roles) && clip.Travel is { } travel
                           && travel.Confidence >= Believable && travel.Speed > 0.5f
                ? travel.Speed
                : null;

            if (Moves(read.Roles) && speeds && speed is null)
                unmeasured.Add($"{clip.Name}: " + (clip.Travel is not { } said
                    ? "nothing in it is ever on the ground"
                    : said.Confidence < Believable
                        ? $"only {said.Confidence:P0} of it is on the ground, too little to read {said.Speed:0.#} from"
                        : quarrel ?? "its feet carry it nowhere"));

            clips.Add(new RoledClip(
                clip,
                new RoledAnimation(clip.Path, read.Roles, Stack: clip.Name, Speed: speed),
                read.Why,
                quarrel));

            durations[clip.Name] = clip.Duration;
        }

        return new RoledClips(clips, durations, disagreements, unmeasured);
    }

    /// <summary>What the name and the feet disagree about, or null where they do not.</summary>
    private static string? Quarrel(ReadRole read, WrittenClip clip)
    {
        // A name that says nothing cannot disagree with anything. It is worth reporting all
        // the same, since a clip nobody can place is one nothing will ever play.
        if (read.Roles.Count == 0) return "nothing in the name says what it is for";

        if (!Moves(read.Roles))
        {
            // A clip that should stand still and does not. The bar is a walking pace: an
            // attack that steps in, or a death that falls over, carries the creature about
            // and is not a fault.
            return clip.Travel is { Speed: > 60f, Confidence: >= Believable } fast
                ? $"read as {read.Roles[0].Kind}, which does not travel, but its feet carry it " +
                  $"{fast.Speed:0.#} units a second"
                : null;
        }

        if (clip.Travel is not { } travel) return "read as locomotion, but nothing in it is ever on the ground";
        if (travel.Confidence < Believable) return null;

        if (travel.Speed < 0.5f)
            return $"read as {read.Roles[0].Kind} {read.Roles[0].Heading}, but its feet do not carry it anywhere";

        Heading wanted = read.Roles.Select(r => r.Heading).FirstOrDefault(h => h != Heading.None);
        if (wanted == Heading.None) return null;

        float should = Degrees(wanted);
        float off = Math.Abs(Wrap(travel.Heading - should));

        return off <= Tolerance
            ? null
            : $"read as {wanted}, which is {should:0} degrees, but its feet carry it at {travel.Heading:0}";
    }

    private static bool Moves(IReadOnlyList<AnimationRole> roles) =>
        roles.Any(r => r.Kind is RoleKind.Walk or RoleKind.Run or RoleKind.Trot or RoleKind.Sprint or RoleKind.Swim);

    /// <summary>
    /// Which way a heading points, in degrees, with the creature facing along zero and a
    /// right turn positive -- the frame the travel is measured in.
    /// </summary>
    private static float Degrees(Heading heading) => heading switch
    {
        Heading.Forward => 0f,
        Heading.ForwardRight => 45f,
        Heading.Right => 90f,
        Heading.BackRight => 135f,
        Heading.Back => 180f,
        Heading.BackLeft => -135f,
        Heading.Left => -90f,
        Heading.ForwardLeft => -45f,
        _ => 0f,
    };

    private static float Wrap(float degrees)
    {
        float at = degrees % 360f;
        if (at > 180f) at -= 360f;
        if (at < -180f) at += 360f;
        return at;
    }
}
