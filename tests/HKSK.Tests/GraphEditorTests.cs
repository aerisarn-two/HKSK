using Xunit;
using HKSK.Behavior;
using HKSK.Havok;
using HKX2;

namespace HKSK.Tests;

/// <summary>
/// Editing a behaviour: what is added reads back as the game's own files read.
/// </summary>
public sealed class GraphEditorTests
{
    private static (SyntheticProject Project, string Behavior) Behavior()
    {
        SyntheticProject project = SyntheticProject.Build();
        string behavior = Directory.EnumerateFiles(project.ProjectFolder, "*.hkx", SearchOption.AllDirectories)
            .First(f => f.Contains("behaviors", StringComparison.OrdinalIgnoreCase));
        return (project, behavior);
    }

    [Fact]
    public void AnEventIsAddedOnceAndKeepsItsIndex()
    {
        using SyntheticProject project = Behavior().Project;
        string behavior = Directory.EnumerateFiles(project.ProjectFolder, "*.hkx", SearchOption.AllDirectories)
            .First(f => f.Contains("behaviors", StringComparison.OrdinalIgnoreCase));

        var editor = new GraphEditor(HavokFile.Load(behavior));
        int before = editor.Strings.m_eventNames.Count;
        int flags = editor.Data.m_eventInfos.Count;

        int added = editor.Event("catNextIdle");

        Assert.Equal(before, added);
        Assert.Equal(added, editor.Event("catNextIdle"));
        Assert.Equal(before + 1, editor.Strings.m_eventNames.Count);

        // An event is a name and a flags entry beside it, added together.
        Assert.Equal(flags + 1, editor.Data.m_eventInfos.Count);

        // One the file already has is found, not added again.
        Assert.Equal(0, editor.Event(editor.Strings.m_eventNames[0]));
    }

    [Fact]
    public void AVariableIsDeclaredWithItsTypeAndInitialValue()
    {
        using SyntheticProject project = Behavior().Project;
        string behavior = Directory.EnumerateFiles(project.ProjectFolder, "*.hkx", SearchOption.AllDirectories)
            .First(f => f.Contains("behaviors", StringComparison.OrdinalIgnoreCase));

        var editor = new GraphEditor(HavokFile.Load(behavior));

        int sneak = editor.IntVariable("iIsInSneak", 1);
        int rate = editor.RealVariable("walkBackRate", 66.79f);
        int flag = editor.BoolVariable("bCatIsLying");

        Assert.Equal(sneak, editor.VariableIndex("iIsInSneak"));
        Assert.Equal((sbyte)VariableType.VARIABLE_TYPE_INT32, editor.Data.m_variableInfos[sneak].m_type);
        Assert.Equal((sbyte)VariableType.VARIABLE_TYPE_REAL, editor.Data.m_variableInfos[rate].m_type);
        Assert.Equal((sbyte)VariableType.VARIABLE_TYPE_BOOL, editor.Data.m_variableInfos[flag].m_type);

        var values = editor.Data.m_variableInitialValues!.m_wordVariableValues;
        Assert.Equal(1, values[sneak].m_value);
        Assert.Equal(66.79f, BitConverter.Int32BitsToSingle(values[rate].m_value), 3);
        Assert.Equal(0, values[flag].m_value);
        Assert.Equal(editor.Strings.m_variableNames.Count, editor.Data.m_variableInfos.Count);
    }

    /// <summary>
    /// A state, a transition and a wildcard, written as the shipped graphs write them,
    /// and read back from the saved file.
    /// </summary>
    [Fact]
    public void AStateAndItsTransitionsSurviveTheFile()
    {
        using SyntheticProject project = Behavior().Project;
        string behavior = Directory.EnumerateFiles(project.ProjectFolder, "*.hkx", SearchOption.AllDirectories)
            .First(f => f.Contains("behaviors", StringComparison.OrdinalIgnoreCase));

        var file = HavokFile.Load(behavior);
        var editor = new GraphEditor(file);

        var machine = editor.StateMachine("CatIdles", randomStartEvent: "catNextIdle");
        var idle = editor.State(machine, "Lick", editor.Clip("Lick", @"Animations\Lick.hkx", triggers: ("catNextIdle", 0f, true)));
        var scratch = editor.State(machine, "Scratch", editor.Clip("Scratch", @"Animations\Scratching.hkx", ClipMode.Looping));

        editor.Transition(idle, "catNextIdle", scratch, editor.Effect("CatBlend", 0.2f));
        editor.Wildcard(machine, "catStop", idle, condition: "Speed < 1");
        editor.EnterEvents(scratch, "HeadTrackingOff");

        // The graph has to reach it, or nothing reads it: the machine replaces the root.
        editor.Graph.m_rootGenerator = machine;
        file.Save(behavior);

        var back = new GraphEditor(HavokFile.Load(behavior));
        var read = back.Require<hkbStateMachine>("CatIdles");

        Assert.Equal(back.EventIndex("catNextIdle"), read.m_randomTransitionEventId);
        Assert.Equal(2, read.m_startStateMode);
        Assert.Equal(32, read.m_maxSimultaneousTransitions);
        Assert.Equal(-1, read.m_syncVariableIndex);

        var states = read.m_states.ToDictionary(s => s.m_name);
        Assert.Equal([0, 1], read.m_states.Select(s => s.m_stateId));
        Assert.Equal(1f, states["Lick"].m_probability);
        Assert.True(states["Lick"].m_enable);
        Assert.Equal("HeadTrackingOff", back.EventName(states["Scratch"].m_enterNotifyEvents!.m_events[0].m_id));

        var clip = (hkbClipGenerator)states["Lick"].m_generator!;
        Assert.Equal(@"Animations\Lick.hkx", clip.m_animationName);
        Assert.Equal(-1, clip.m_animationBindingIndex);
        Assert.Equal("catNextIdle", back.EventName(clip.m_triggers!.m_triggers[0].m_event.m_id));
        Assert.True(clip.m_triggers.m_triggers[0].m_relativeToEndOfClip);
        Assert.Equal(1, ((hkbClipGenerator)states["Scratch"].m_generator!).m_mode);

        // An unconditioned transition says so with the flag the shipped ones carry, and a
        // wildcard with the local-wildcard flag beside its condition.
        var transition = states["Lick"].m_transitions!.m_transitions[0];
        Assert.Equal(0x100, transition.m_flags & 0x100);
        Assert.Equal(1, transition.m_toStateId);
        Assert.Equal(-1, transition.m_triggerInterval.m_enterEventId);
        Assert.Equal(0.2f, ((hkbBlendingTransitionEffect)transition.m_transition!).m_duration);

        var wildcard = read.m_wildcardTransitions!.m_transitions[0];
        Assert.Equal(0x800, wildcard.m_flags & 0x800);
        Assert.Equal(0, wildcard.m_flags & 0x100);
        Assert.Equal("Speed < 1", ((hkbExpressionCondition)wildcard.m_condition!).m_expression);
    }

    [Fact]
    public void RemovingAStateTakesTheTransitionsIntoItWithIt()
    {
        using SyntheticProject project = Behavior().Project;
        string behavior = Directory.EnumerateFiles(project.ProjectFolder, "*.hkx", SearchOption.AllDirectories)
            .First(f => f.Contains("behaviors", StringComparison.OrdinalIgnoreCase));

        var editor = new GraphEditor(HavokFile.Load(behavior));
        var machine = editor.StateMachine("CatModes");
        var standing = editor.State(machine, "Standing", editor.Clip("Stand", @"Animations\Idle.hkx", ClipMode.Looping));
        var lying = editor.State(machine, "Lying", editor.Clip("Lie", @"Animations\Lie.hkx", ClipMode.Looping));

        editor.Transition(standing, "lieDown", lying);
        editor.Wildcard(machine, "lieDownNow", lying);
        editor.Transition(lying, "getUp", standing);

        Assert.True(editor.RemoveState(machine, "Lying"));

        Assert.Single(machine.m_states);
        Assert.Empty(standing.m_transitions!.m_transitions);
        Assert.Empty(machine.m_wildcardTransitions!.m_transitions);
        Assert.False(editor.RemoveState(machine, "Lying"));
        Assert.Equal(standing.m_stateId, machine.m_startStateId);
    }

    [Fact]
    public void ABlendIsParametricAndBoundToItsVariable()
    {
        using SyntheticProject project = Behavior().Project;
        string behavior = Directory.EnumerateFiles(project.ProjectFolder, "*.hkx", SearchOption.AllDirectories)
            .First(f => f.Contains("behaviors", StringComparison.OrdinalIgnoreCase));

        var editor = new GraphEditor(HavokFile.Load(behavior));
        var blend = editor.Blend("TurnBlend", "TurnDeltaDamped",
            (editor.Clip("Left", @"Animations\WalkL.hkx", ClipMode.Looping), 90f),
            (editor.Clip("Forward", @"Animations\Walk.hkx", ClipMode.Looping), 0f),
            (editor.Clip("Right", @"Animations\WalkR.hkx", ClipMode.Looping), -90f));

        Assert.Equal(0x11, blend.m_flags);
        Assert.Equal(-1, blend.m_indexOfSyncMasterChild);
        Assert.Equal([90f, 0f, -90f], blend.m_children.Select(c => c.m_weight));
        Assert.All(blend.m_children, c => Assert.Equal(1f, c.m_worldFromModelWeight));

        var binding = blend.m_variableBindingSet!.m_bindings.Single();
        Assert.Equal("blendParameter", binding.m_memberPath);
        Assert.Equal(editor.VariableIndex("TurnDeltaDamped"), binding.m_variableIndex);
        Assert.Equal(-1, binding.m_bitIndex);
    }
}
