using HKSK.Cache;
using HKSK.Model;

namespace HKSK.Tests;

/// <summary>
/// Guesses which compass a locomotion state's records were sampled from.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is test scaffolding, not a fact about the format, and it is why
/// it lives here rather than in the library.</strong> <c>HKSK.Cache</c> reads and
/// writes the cache files and reads the graph; <see cref="SpeedDataFile"/> is the
/// speed table exactly as <see cref="AnimationDataFile"/> and
/// <see cref="AnimationSetDataFile"/> are theirs, and <see cref="SpeedSampler"/>
/// reports what the graph says and nothing more. None of them should be inferring
/// anything, and a caller that knows the answer -- a project supplying the
/// state-to-family mapping properly -- should not have to route around a guess
/// baked into the library.
/// </para>
/// <para>
/// What the graph does say is used first and is not guesswork: a
/// <c>BSiStateTaggingGenerator</c> that sets <c>iState</c> to the key has that
/// key's compasses beneath it (<see cref="SpeedSampler.TaggedCompasses"/>). The
/// guessing starts only where that leaves a choice, and stops rather than pick
/// between equals.
/// </para>
/// </remarks>
internal static class FamilyGuess
{
    /// <summary>
    /// The compass answering a state at a speed and a heading, given the movement
    /// types. <strong>No rule using them has survived measurement, so this is
    /// <see cref="CompassFor"/>.</strong>
    /// </summary>
    /// <remarks>
    /// <para>
    /// The parameters are kept because the shape of the answer is known to need
    /// them: some records change family partway up their range, and nothing in the
    /// graph says where. The movement type is the obvious place to look, since it
    /// states a walk speed and a run speed per heading, and it is an input the
    /// caller has and the library does not.
    /// </para>
    /// <para>
    /// Two rules were written against it and both were measured and dropped:
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// <em>above the walk speed the creature is running, so the running family
    /// answers</em> — costs nineteen curves on the giant alone.
    /// <c>GiantCombatWalk</c> asks for 82.46 walking and 247.37 running, and those
    /// are the second and third rungs of <em>one</em> ladder rather than a switch
    /// between two families.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// the same, but only where the state's own compass <em>cannot reach</em> the
    /// run speed and another one can — which spares the giant but still costs
    /// seven curves on the player and the benthic lurker, and gains nothing. Not
    /// even the falmer, whose records do span a switch: its ladder reaches
    /// 175.774, its run speed exactly, so a reach test never fires for it.
    /// </description>
    /// </item>
    /// </list>
    /// <para>
    /// The falmer's switch is therefore not a gait change in the movement type's
    /// sense, and what it is remains open. Answering with one compass throughout
    /// scores 660 of 1482; both rules score less.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<(float Direction, SpeedLadder Ladder)>? CompassAt(
        this SpeedSampler sampler, int key, float x, float direction,
        IReadOnlyDictionary<string, MovementType> movements)
        => sampler.CompassFor(key);

    /// <summary>
    /// The compass serving a state, chosen by matching its rungs against the
    /// movement type's speeds -- a second opinion on <see cref="CompassFor(SpeedSampler, int)"/>,
    /// arrived at without reading a single name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A ladder's rungs are placed at the speeds the movement type asks for, so a
    /// state and the compass that answers it can be paired by their numbers rather
    /// than by their names. <c>FalmerDefault</c> walks forward at 100.44 and runs at
    /// 361.24, and exactly one of the falmer's four compasses has both of those as
    /// rungs — <c>MagicCast_DirectionalBlend</c>, whose name shares not one token
    /// with the state's. <c>GiantCombatWalk</c> is 82.46/247.37 against rungs
    /// 82.458/247.374, and <c>GiantCombatRun</c> is 50/415 against 50/415.
    /// </para>
    /// <para>
    /// Each of the movement type's eight numbers is looked for in the arm at its own
    /// heading, so a compass scores on the whole shape and not on one lucky rung.
    /// Where the numbers cannot separate the compasses — the benthic lurker's
    /// <c>DirectionalBlend</c> and <c>CombatDirectionalBlend_WALK</c> have identical
    /// rungs, and its two states identical speeds — the name heuristic breaks the
    /// tie, as it does when no movement type is supplied at all.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<(float Direction, SpeedLadder Ladder)>? CompassFor(
        this SpeedSampler sampler, int key, IReadOnlyDictionary<string, MovementType> movements)
        => sampler.CompassBySpeeds(key, movements)?.Arms ?? sampler.CompassFor(key);

    /// <summary>
    /// The compass whose rungs are the state's movement-type speeds, or null where
    /// the numbers decide nothing -- no movement type is known, none of its speeds
    /// is a rung anywhere, or two compasses match it equally well.
    /// </summary>
    public static SpeedCompass? CompassBySpeeds(
        this SpeedSampler sampler, int key, IReadOnlyDictionary<string, MovementType> movements)
    {
        SpeedState state = sampler.States.FirstOrDefault(s => s.Key == key);
        if (state.MovementTypes is null) return null;

        SpeedCompass best = default;
        bool found = false;
        int most = 0;
        bool tied = false;

        foreach (string name in state.MovementTypes)
        {
            if (!movements.TryGetValue(name, out MovementType movement)) continue;

            foreach (SpeedCompass compass in sampler.Compasses)
            {
                int score = Score(compass, movement);
                if (score == 0) continue;

                if (score > most) { most = score; best = compass; found = true; tied = false; }
                else if (score == most && compass.Name != best.Name) tied = true;
            }
        }

        return found && !tied ? best : null;
    }

    /// <summary>How many of a movement type's eight speeds are rungs of a compass.</summary>
    private static int Score(SpeedCompass compass, MovementType movement)
    {
        int score = 0;

        for (int quarter = 0; quarter < 4; quarter++)
        {
            float heading = quarter * 0.25f;

            SpeedLadder? ladder = null;
            foreach ((float at, SpeedLadder arm) in compass.Arms)
                if (MathF.Abs(at - heading) < 1e-3f) ladder = arm;

            if (ladder is null) continue;

            (float walking, float running) = movement.At(heading);
            foreach (SpeedRung rung in ladder.Rungs)
            {
                if (MathF.Abs(rung.Weight - walking) <= 0.05f) score++;
                if (MathF.Abs(rung.Weight - running) <= 0.05f) score++;
            }
        }

        return score;
    }

    /// <summary>The compass serving a state, or null when it cannot be told.</summary>
    public static IReadOnlyList<(float Direction, SpeedLadder Ladder)>? CompassFor(
        this SpeedSampler sampler, int key)
    {
        if (sampler.Compasses.Count == 0) return null;

        // Where the graph tags this key, those nodes are the candidates and the
        // name only chooses among them. Widening back to every compass of the same
        // name undoes the tag: the player's key 8 is NPCBow, whose tag reaches the
        // bow and crossbow compasses of 1hm_locomotion, while the copy in
        // bow_direction_behavior is the drawn-bow locomotion answering key 3.
        sampler.TaggedCompasses.TryGetValue(key, out IReadOnlyList<SpeedCompass>? tagged);
        List<SpeedCompass> candidates = tagged is { Count: > 0 } ? [.. tagged] : [.. sampler.Compasses];

        if (candidates.Count == 1) return candidates[0].Arms;

        var movements = sampler.States.FirstOrDefault(s => s.Key == key).MovementTypes ?? [];
        if (movements.Count == 0) return Pick(candidates);

        // Every movement type of a creature carries its name, so the tokens they
        // all share separate nothing -- unless removing them leaves nothing at all,
        // which is the case for a creature with one movement type.
        HashSet<string>? shared = null;
        foreach (SpeedState state in sampler.States)
            foreach (string movement in state.MovementTypes)
            {
                HashSet<string> tokens = Tokenise(movement);
                if (shared is null) shared = tokens;
                else shared.IntersectWith(tokens);
            }

        var wanted = new HashSet<string>();
        foreach (string movement in movements) wanted.UnionWith(Tokenise(movement));
        if (shared is not null && !wanted.All(shared.Contains)) wanted.ExceptWith(shared);

        // A name can occur in several of a project's graphs, so rank names and not
        // nodes, or identical copies tie with each other and look ambiguous.
        var ranked = candidates
            .GroupBy(c => c.Name)
            .Select(g =>
            {
                HashSet<string> tokens = Tokenise(g.Key);
                return (Copies: g.ToList(),
                        Score: tokens.Count(t => wanted.Contains(t)) * 10 - tokens.Count(t => !wanted.Contains(t)));
            })
            .OrderByDescending(c => c.Score)
            .ToList();

        if (ranked.Count == 0) return null;
        if (ranked.Count > 1 && ranked[0].Score <= ranked[1].Score) return null;

        return Pick(ranked[0].Copies);
    }

    /// <summary>One compass out of the copies defined under the same name.</summary>
    /// <remarks>
    /// Copies that play the same thing are the same answer. Where they differ the
    /// file decides: Bethesda split a graph along the lines the game switches on,
    /// so <c>Bow_Direction_Blend</c> in <c>bow_direction_behavior</c> is the
    /// drawn-bow locomotion while the copy in <c>1hm_locomotion</c> is the bow
    /// merely equipped, and they are different curves.
    /// </remarks>
    private static IReadOnlyList<(float Direction, SpeedLadder Ladder)>? Pick(List<SpeedCompass> copies)
    {
        if (copies.Count == 0) return null;

        var distinct = copies
            .GroupBy(c => string.Join(";", c.Arms.OrderBy(a => a.Direction)
                .Select(a => $"{a.Direction}:{string.Join(",", a.Ladder.Rungs.Select(r => $"{r.Weight}/{r.Animation}"))}")))
            .ToList();

        if (distinct.Count == 1) return distinct[0].First().Arms;

        var own = copies
            .Where(c => c.File is not null)
            .Select(c => (c, Shared: Tokenise(c.Name).Intersect(Tokenise(c.File!)).Count()))
            .OrderByDescending(t => t.Shared)
            .ToList();

        if (own.Count == 0 || own[0].Shared == 0) return null;
        if (own.Count > 1 && own[1].Shared == own[0].Shared) return null;

        return own[0].c.Arms;
    }

    /// <summary>Splits a node or movement-type name into comparable tokens.</summary>
    /// <remarks>
    /// The two sides were written by different people: a movement type says what
    /// the actor is doing and a node says what it plays, so they differ by
    /// inflection. And <c>MT</c> is the movement type itself, so an <c>MT_</c>
    /// blend is the default one.
    /// </remarks>
    private static HashSet<string> Tokenise(string name)
    {
        string[] parts = System.Text.RegularExpressions.Regex.Split(
            name, @"(?<!^)(?=[A-Z][a-z])|[^A-Za-z0-9]+|(?<=[a-z])(?=[0-9])|(?<=[0-9])(?=[A-Za-z])");

        HashSet<string> noise = ["blend", "direction", "directional", "locomotion", "behavior", ""];

        var tokens = new HashSet<string>();
        foreach (string part in parts)
        {
            string token = Stem(part.ToLowerInvariant());
            if (token.Length > 0 && !noise.Contains(token)) tokens.Add(token);
        }

        return tokens;
    }

    private static string Stem(string word) => word switch
    {
        "mt" => "default",
        _ when word.Length > 5 && word.EndsWith("ing", StringComparison.Ordinal) => word[..^3],
        _ when word.Length > 4 && word.EndsWith("ed", StringComparison.Ordinal) => word[..^2],
        _ when word.Length > 4 && word.EndsWith('s') => word[..^1],
        _ => word,
    };
}
