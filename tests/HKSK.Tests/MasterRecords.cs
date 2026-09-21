using HKSK.Records;
using Loqui;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using ActionRecord = HKSK.Records.ActionRecord;

namespace HKSK.Tests;

/// <summary>
/// The masters' records, read the way a caller of the library reads them.
/// </summary>
/// <remarks>
/// <para>
/// <c>HKSK</c> does not open plugins; it states what it needs as <see cref="IGameRecords"/>
/// and <c>SKAssets</c> supplies it from a load order -- its <c>GameRecordReader</c>, which
/// the <c>setgen</c> and <c>speedgen</c> tools there run on. The tests measure against the
/// shipped game and cannot reference <c>SKAssets</c>, which depends on this library, so they
/// read the masters here. It reads records and decides nothing: what they mean is
/// <see cref="GameRecordRules"/>'.
/// </para>
/// <para>
/// The five masters are opened directly and in order -- <c>GameEnvironment.Typical</c>
/// wants the <c>plugins.txt</c> a launcher writes, which a machine that never ran the game
/// does not have -- later ones overriding earlier, which is all a load order does for
/// records nothing else touches.
/// </para>
/// </remarks>
public static class MasterRecords
{
    /// <summary>The masters, in load order.</summary>
    public static readonly string[] Order =
        ["Skyrim.esm", "Update.esm", "Dawnguard.esm", "HearthFires.esm", "Dragonborn.esm"];

    /// <summary>Reads the masters in a Data folder.</summary>
    public static GameRecords Read(string dataFolder)
    {
        var movements = new Dictionary<FormKey, MovementTypeRecord>();
        var races = new Dictionary<FormKey, RaceRecord>();
        var idles = new Dictionary<FormKey, IdleRecord>();
        var actions = new Dictionary<FormKey, ActionRecord>();

        foreach (string master in Order)
        {
            string path = Path.Combine(dataFolder, master);
            if (!File.Exists(path)) continue;

            using var mod = SkyrimMod.CreateFromBinaryOverlay(path, SkyrimRelease.SkyrimSE);

            foreach (IMovementTypeGetter m in mod.MovementTypes)
                movements[m.FormKey] = new MovementTypeRecord
                {
                    Id = m.FormKey.ToString(), EditorID = m.EditorID, Name = m.Name,
                    ForwardWalk = m.ForwardWalk, ForwardRun = m.ForwardRun,
                    BackWalk = m.BackWalk, BackRun = m.BackRun,
                    LeftWalk = m.LeftWalk, LeftRun = m.LeftRun,
                    RightWalk = m.RightWalk, RightRun = m.RightRun,
                };

            foreach (IRaceGetter r in mod.Races)
            {
                var defaults = new Dictionary<MovementRole, string>();
                foreach ((MovementRole role, IFormLinkNullableGetter<IMovementTypeGetter> link) in new[]
                         {
                             (MovementRole.Walk, r.BaseMovementDefaultWalk), (MovementRole.Run, r.BaseMovementDefaultRun),
                             (MovementRole.Swim, r.BaseMovementDefaultSwim), (MovementRole.Fly, r.BaseMovementDefaultFly),
                             (MovementRole.Sneak, r.BaseMovementDefaultSneak), (MovementRole.Sprint, r.BaseMovementDefaultSprint),
                         })
                    if (!link.IsNull) defaults[role] = link.FormKey.ToString();

                races[r.FormKey] = new RaceRecord
                {
                    Id = r.FormKey.ToString(), EditorID = r.EditorID,
                    MaleBehavior = r.BehaviorGraph.Male?.File.GivenPath,
                    FemaleBehavior = r.BehaviorGraph.Female?.File.GivenPath,
                    AttackEvents = [.. r.Attacks.Select(a => a.AttackEvent).OfType<string>()],
                    DefaultMovements = defaults,
                };
            }

            foreach (IIdleAnimationGetter i in mod.IdleAnimations)
                idles[i.FormKey] = new IdleRecord
                {
                    Id = i.FormKey.ToString(), EditorID = i.EditorID,
                    AnimationEvent = string.IsNullOrEmpty(i.AnimationEvent) ? null : i.AnimationEvent,
                    BehaviorFile = i.Filename?.GivenPath,
                    Parent = Link(i, 0), PreviousSibling = Link(i, 1),
                    Conditions = [.. i.Conditions.Select(Condition)],
                };

            foreach (IActionRecordGetter a in mod.Actions)
                actions[a.FormKey] = new ActionRecord { Id = a.FormKey.ToString(), EditorID = a.EditorID };
        }

        return new GameRecords
        {
            MovementTypes = [.. movements.Values],
            Races = [.. races.Values],
            Idles = [.. idles.Values],
            Actions = [.. actions.Values],
        };

        static string? Link(IIdleAnimationGetter idle, int slot) =>
            slot < idle.RelatedIdles.Count && !idle.RelatedIdles[slot].IsNull
                ? idle.RelatedIdles[slot].FormKey.ToString()
                : null;
    }

    /// <summary>A condition by its function's editor name: <c>IsSprintingConditionData</c> is <c>IsSprinting</c>.</summary>
    internal static IdleCondition Condition(IConditionGetter c)
    {
        string type = ((ILoquiObject)c.Data).Registration.Name;
        string function = type.EndsWith("ConditionData", StringComparison.Ordinal) ? type[..^"ConditionData".Length] : type;

        return new IdleCondition(
            function,
            (ConditionOperator)(int)c.CompareOperator,
            c is IConditionFloatGetter f ? f.ComparisonValue : float.NaN,
            c.Flags.HasFlag(Mutagen.Bethesda.Skyrim.Condition.Flag.OR));
    }
}
