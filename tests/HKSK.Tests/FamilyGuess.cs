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
    /// A record answered by a sequence of ladders, because the creature changes
    /// gait partway up its own speed range.
    /// </summary>
    /// <param name="Project">The project whose table carries it.</param>
    /// <param name="Key">The locomotion state.</param>
    /// <param name="Stages">
    /// The compasses in increasing speed, each with the speed it takes over at. The
    /// first stage's threshold is ignored.
    /// </param>
    /// <remarks>
    /// <strong>The thresholds are not in the behaviour graph.</strong> The gait
    /// machines transition on the events <c>runStart</c> and <c>walkStart</c>, with
    /// no condition attached -- the game raises them from the actor's movement
    /// type, and the graph only responds. So this is external data of the same kind
    /// as the state-to-family mapping, and it sits here for the same reason: to be
    /// replaced by a project that supplies it, not to be guessed at in a library.
    /// </remarks>
    internal readonly record struct GaitSequence(string Project, int Key, (float At, string Compass)[] Stages);

    /// <summary>The gait sequences visible in the shipped table.</summary>
    /// <remarks>
    /// The falmer walks and then runs. The benthic lurker has three: its plain walk,
    /// its combat walk and its combat run. At heading 0 the first two deliver the
    /// same thing, which is what hid the middle stage until a sideways heading was
    /// looked at -- there the plain walk saturates at 99.5 and the combat walk
    /// carries on to 129.4.
    /// </remarks>
    private static readonly GaitSequence[] Sequences =
    [
        new("FalmerProject", 2,
        [
            (0f, "MT_DirectionalBlend"),
            (100.25f, "1HM_DirectionalBlend_Run"),
        ]),
        new("BenthicLurkerProject", 1,
        [
            (0f, "DirectionalBlend"),
            (115f, "CombatDirectionalBlend_WALK"),
            (215.75f, "CombatDirectionalBlend_RUN"),
        ]),
    ];

    /// <summary>The compass answering a state at a given speed.</summary>
    /// <remarks>
    /// The same as <see cref="CompassFor"/> for a state that keeps one gait, which
    /// is nearly all of them.
    /// </remarks>
    public static IReadOnlyList<(float Direction, SpeedLadder Ladder)>? CompassAt(
        this SpeedSampler sampler, string project, int key, float x)
    {
        foreach (GaitSequence g in Sequences)
        {
            if (g.Project != project || g.Key != key) continue;

            string wanted = g.Stages[0].Compass;
            foreach ((float at, string compass) in g.Stages)
                if (x >= at) wanted = compass;

            foreach (SpeedCompass c in sampler.Compasses)
                if (c.Name == wanted) return c.Arms;
        }

        return sampler.CompassFor(key);
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
