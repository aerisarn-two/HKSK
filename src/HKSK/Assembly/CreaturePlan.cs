namespace HKSK.Assembly;

/// <summary>How a creature moves, which decides the shape of its locomotion.</summary>
/// <remarks>
/// Measured over the game's 46 creatures (<c>docs/creature-patterns-research.md</c>).
/// The plan is read off the roles rather than asked for, because the roles already
/// say it: a creature with a walk at eight headings is a compass biped whether or
/// not anybody calls it one.
/// </remarks>
public enum LocomotionPlan
{
    /// <summary>Nothing walks: the roles do not reach the floor.</summary>
    None,

    /// <summary>A compass biped: eight headings, 14 of the game's creatures.</summary>
    CompassBiped,

    /// <summary>Four arms -- forward, back, left, right -- 12 creatures.</summary>
    FourArm,

    /// <summary>A quadruped: a forward clip with left and right variants, or a trot. 13.</summary>
    Quadruped,

    /// <summary>Forward and nothing else, 4 creatures.</summary>
    ForwardOnly,

    /// <summary>A swimmer: headings in the water and no walk at all, 3.</summary>
    Swimmer,
}

/// <summary>A part of a creature that is built only when the animations for it are there.</summary>
public enum Module
{
    Locomotion, TurnInPlace, CannedTurn, CombatStance, EquipTransitions,
    Attacking, Bashing, Blocking, Recoil, Stagger,
    Death, GetUp, Reanimate, AggroWarning, BleedOut,
    Swimming, Idles, Feeding, LyingDown, Casting, PairedKillMoves,
}

/// <summary>One rung of a speed ladder: what plays, and how fast it carries the creature.</summary>
public sealed record Rung(string Animation, float Speed)
{
    public override string ToString() => $"{Animation} at {Speed:F1}";
}

/// <summary>
/// What will be built, read off the roles, before anything is written.
/// </summary>
/// <param name="Locomotion">The shape the moving part takes.</param>
/// <param name="Modules">The parts the animations given are enough for.</param>
/// <param name="Attacks">The attack events the graph will answer to.</param>
/// <param name="Headings">The headings the locomotion covers.</param>
/// <param name="Notes">What was read, and what was left out for want of an animation.</param>
/// <param name="Synthesised">
/// What will be made rather than taken from an animation given. A creature with no
/// turn clip gets one from its idle and a turn nobody animated, which is a decision
/// worth seeing before it is made.
/// </param>
/// <param name="Refusals">Why this cannot be built at all. Empty is buildable.</param>
public sealed record CreaturePlan(
    LocomotionPlan Locomotion,
    IReadOnlyList<Module> Modules,
    IReadOnlyList<string> Attacks,
    IReadOnlyList<Heading> Headings,
    IReadOnlyList<string> Notes,
    IReadOnlyList<string> Synthesised,
    IReadOnlyList<string> Refusals)
{
    public bool CanBuild => Refusals.Count == 0;

    public override string ToString() => CanBuild
        ? $"{Locomotion} with {Modules.Count} modules and {Attacks.Count} attacks"
        : $"cannot be built: {string.Join("; ", Refusals)}";
}

/// <summary>
/// Reads a set of roled animations and says what can be made of them.
/// </summary>
/// <remarks>
/// <para>
/// Pure, and deliberately so. A front end shows this to whoever is making the
/// creature before a byte is written, and the answer to "why has my creature no
/// combat stance" is a note in it rather than a silence in the graph.
/// </para>
/// <para>
/// The floor is the witchlight's: an idle and a forward walk. Below that there is
/// no creature to build, and saying so is more useful than building one that
/// stands still.
/// </para>
/// </remarks>
public static class CreaturePlanner
{
    /// <summary>What can be made of these animations.</summary>
    public static CreaturePlan Of(IReadOnlyList<RoledAnimation> animations)
    {
        ArgumentNullException.ThrowIfNull(animations);

        var notes = new List<string>();
        var synthesised = new List<string>();
        var refusals = new List<string>();
        var modules = new List<Module>();

        var roles = animations.SelectMany(a => a.Roles.Select(r => (Animation: a, Role: r))).ToList();
        bool Any(RoleKind kind) => roles.Any(r => r.Role.Kind == kind);
        var headings = roles
            .Where(r => r.Role.Kind is RoleKind.Walk or RoleKind.Run or RoleKind.Trot or RoleKind.Sprint)
            .Select(r => r.Role.Heading)
            .Where(h => h != Heading.None)
            .Distinct()
            .OrderBy(h => (int)h)
            .ToList();

        var swimHeadings = roles.Where(r => r.Role.Kind == RoleKind.Swim).Select(r => r.Role.Heading).Distinct().ToList();

        // ---- the floor
        if (!Any(RoleKind.Idle)) refusals.Add("no animation is the idle, and every creature stands somewhere");
        if (headings.Count == 0 && swimHeadings.Count == 0)
            refusals.Add("no animation carries the creature anywhere: a forward walk is the least there can be");
        else if (headings.Count > 0 && !headings.Contains(Heading.Forward))
            refusals.Add($"nothing walks forward; the headings given are {string.Join(", ", headings)}");

        // ---- the plan
        LocomotionPlan plan =
            headings.Count == 0 && swimHeadings.Count > 0 ? LocomotionPlan.Swimmer
            : headings.Count >= 8 ? LocomotionPlan.CompassBiped
            : headings.Count >= 4 ? LocomotionPlan.FourArm
            : roles.Any(r => r.Role.Kind == RoleKind.Trot)
              || roles.Any(r => r.Role.Kind is RoleKind.Walk or RoleKind.Run && r.Role.Side != Side.None)
                ? LocomotionPlan.Quadruped
            : headings.Count > 0 ? LocomotionPlan.ForwardOnly
            : LocomotionPlan.None;

        if (refusals.Count > 0) return new CreaturePlan(plan, modules, [], headings, notes, synthesised, refusals);

        modules.Add(Module.Locomotion);
        notes.Add(plan switch
        {
            LocomotionPlan.CompassBiped => $"a compass biped: {headings.Count} headings",
            LocomotionPlan.FourArm => $"four arms: {string.Join(", ", headings)}",
            LocomotionPlan.Quadruped => "a quadruped: a forward clip with its left and right variants",
            LocomotionPlan.ForwardOnly => "forward and nothing else, which four of the game's creatures do",
            LocomotionPlan.Swimmer => $"a swimmer: {swimHeadings.Count} headings in the water and no walk",
            _ => "no locomotion",
        });

        // ---- the ladder
        int gaits = new[] { RoleKind.Walk, RoleKind.Trot, RoleKind.Run, RoleKind.Sprint }.Count(Any);
        notes.Add(gaits == 1
            ? "one gait, so the ladder is two rungs: a creep and the walk"
            : $"{gaits} gaits, so the ladder has {gaits + 1} rungs counting the creep");

        // ---- the modules, each present exactly when its animations are
        void Built(Module module, bool present, string had, string lacked)
        {
            if (present) { modules.Add(module); notes.Add(had); }
            else notes.Add(lacked);
        }

        // A turn in place is the one part of a creature that can be made rather than
        // given. The game's own are the idle pose with a rotation and no travel -- the
        // sabre cat's turns 87 degrees over half a second and goes nowhere -- so a
        // creature with no turn clip is given its idle in a second slot and a turn
        // rate. The feet do not shuffle; the creature turns.
        var turns = roles.Where(r => r.Role.Kind == RoleKind.TurnInPlace).ToList();
        modules.Add(Module.TurnInPlace);
        if (turns.Count > 0)
            notes.Add(turns.Any(t => t.Role.Mirror) || turns.Select(t => t.Role.Side).Distinct().Count() > 1
                ? "turning in place, both ways"
                : "turning in place, one way only, which will be mirrored");
        else
        {
            notes.Add("no turn in place was given, so one is made from the idle");
            synthesised.Add("a turn in place each way: the idle in a slot of its own, with a turn and no travel");
        }

        var canned = roles.Where(r => r.Role.Kind == RoleKind.CannedTurn).ToList();
        Built(Module.CannedTurn, canned.Count > 0,
            $"canned turns at {string.Join(" and ", canned.Select(c => c.Role.Angle).Distinct().Order())} degrees",
            "no canned turns: the engine's cannedTurn events will fall on the floor, as the witchlight's do");

        var attacks = roles.Where(r => r.Role.Kind is RoleKind.Attack or RoleKind.PowerAttack)
            .Select(r => r.Role.Name ?? r.Animation.Stem)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        Built(Module.Attacking, attacks.Count > 0,
            $"{attacks.Count} attacks: {string.Join(", ", attacks)}",
            "no attacks: the creature will never strike");

        Built(Module.Recoil, Any(RoleKind.Recoil), "recoiling", "no recoil");
        var staggers = roles.Where(r => r.Role.Kind == RoleKind.Stagger).ToList();
        Built(Module.Stagger, staggers.Count > 0,
            staggers.Count == 1 ? "one stagger" : $"{staggers.Count} staggers, blended on staggerMagnitude",
            "no stagger");

        Built(Module.Death, Any(RoleKind.Death),
            "a death animation, so the creature animates to its ragdoll",
            "no death animation: the creature goes straight to ragdoll, as nine of the game's do");

        var getUps = roles.Count(r => r.Role.Kind == RoleKind.GetUp);
        Built(Module.GetUp, getUps > 0,
            $"getting up, from {getUps} {(getUps == 1 ? "pose" : "poses")}",
            "no get-up: a knocked-down creature stays down");
        Built(Module.Reanimate, Any(RoleKind.Reanimate),
            "reanimating", "no reanimate: the get-up serves for both, where there is one");

        bool combat = roles.Any(r => r.Role.Kind == RoleKind.CombatIdle)
            || roles.Any(r => r.Role.Stance == Stance.Combat);
        Built(Module.CombatStance, combat,
            "a combat stance, entered on weapEquip", "no combat stance: the creature fights as it stands");
        Built(Module.EquipTransitions, Any(RoleKind.Equip) || Any(RoleKind.Unequip),
            "equip and unequip transitions", "no equip transitions: the stance is entered directly");

        Built(Module.Bashing, Any(RoleKind.Bash), "bashing", "no bash");
        Built(Module.Blocking, Any(RoleKind.Block), "blocking", "no block");
        Built(Module.AggroWarning, Any(RoleKind.AggroWarning), "an aggro warning", "no aggro warning");
        Built(Module.BleedOut, Any(RoleKind.BleedOutEnter) || Any(RoleKind.BleedOutLoop),
            "bleeding out", "no bleed-out");
        Built(Module.Swimming, swimHeadings.Count > 0 && headings.Count > 0,
            "swimming beside the walk", "no swimming");
        Built(Module.Idles, Any(RoleKind.IdleVariant),
            $"{roles.Count(r => r.Role.Kind == RoleKind.IdleVariant)} idle variants to choose between",
            "one idle only");
        Built(Module.Feeding, Any(RoleKind.Feed), "feeding", "no feeding");
        Built(Module.LyingDown, Any(RoleKind.LayDown) || Any(RoleKind.LayLoop),
            "lying down", "no lying down");
        Built(Module.Casting, Any(RoleKind.UpperBodyCast), "casting", "no casting");
        Built(Module.PairedKillMoves, Any(RoleKind.KillMoveVictim),
            "paired kill moves, as the victim", "no paired kill moves: the creature cannot be killmoved");

        return new CreaturePlan(plan, modules, attacks, headings, notes, synthesised, refusals);
    }
}
