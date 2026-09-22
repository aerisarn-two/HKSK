using System.Reflection;
using HKSK.Havok;
using HKX2;

namespace HKSK.Behavior;

/// <summary>How a clip generator plays its animation.</summary>
public enum ClipMode : sbyte
{
    SinglePlay = 0,
    Looping = 1,
    UserControlled = 2,
    PingPong = 3,
    Count = 4,
}

/// <summary>
/// Edits one behaviour file's graph in place: its events and variables, and the
/// states, transitions and generators under its state machines.
/// </summary>
/// <remarks>
/// <para>
/// Everything here writes what the game's own files write, field for field, as
/// read off the sabre cat's graphs: a transition with no condition carries the
/// flag that disables its condition, a wildcard the local-wildcard flag, an
/// unconditioned blend the sync and parametric flags, a clip a binding index
/// of -1, a variable a role of nothing. The defaults are not Havok's defaults;
/// they are what a Bethesda graph looks like, so that a state added here is
/// indistinguishable from one the exporter wrote.
/// </para>
/// <para>
/// Events and variables are per file. A graph joined to this one through a
/// behaviour reference has its own table, and an event sent from here reaches
/// it by name, which the game resolves through the character's own pool; so
/// adding an event to a file is adding a name, and the same name in two files
/// is the same event.
/// </para>
/// <para>
/// Nothing is saved: the caller saves the <see cref="File"/> when it is done,
/// and <see cref="HavokFile.All{T}"/> keeps answering for the objects the file
/// was loaded with. What this adds is reached through the graph, which is what
/// the writer walks.
/// </para>
/// </remarks>
public sealed class GraphEditor
{
    /// <summary>The flag that turns a transition's condition off: every unconditioned vanilla transition has it.</summary>
    private const short DisableCondition = 0x100;

    /// <summary>A transition that fires from any state of its machine.</summary>
    private const short LocalWildcard = 0x800;

    /// <summary>The transition enters a nested state of the target, named by <c>m_toNestedStateId</c>.</summary>
    private const short ToNestedStateIdIsValid = 0x2000;

    /// <summary>A blend that syncs its children and reads its parameter: the locomotion blends.</summary>
    private const short SyncParametricBlend = 0x11;

    public GraphEditor(HavokFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        File = file;
        Graph = file.First<hkbBehaviorGraph>()
            ?? throw new InvalidDataException($"'{file.Path}' holds no behaviour graph");
        Data = Graph.m_data ?? throw new InvalidDataException($"'{file.Path}' has a graph without data");
        Strings = Data.m_stringData ?? throw new InvalidDataException($"'{file.Path}' has a graph without names");
    }

    public HavokFile File { get; }

    public hkbBehaviorGraph Graph { get; }

    public hkbBehaviorGraphData Data { get; }

    public hkbBehaviorGraphStringData Strings { get; }

    // ------------------------------------------------------------ events and variables

    /// <summary>The index of an event, or -1.</summary>
    public int EventIndex(string name) => IndexOf(Strings.m_eventNames, name);

    /// <summary>The index of an event, added when the file lacks it.</summary>
    public int Event(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        int index = EventIndex(name);
        if (index >= 0) return index;

        Strings.m_eventNames = Append(Strings.m_eventNames, name);
        Data.m_eventInfos = Append(Data.m_eventInfos, new hkbEventInfo { m_flags = 0 });
        return Strings.m_eventNames.Count - 1;
    }

    /// <summary>The index of a variable, or -1.</summary>
    public int VariableIndex(string name) => IndexOf(Strings.m_variableNames, name);

    /// <summary>
    /// The index of a variable, added with a type and an initial value when the
    /// file lacks it. A variable that exists keeps what it has.
    /// </summary>
    /// <param name="initialBits">
    /// The initial value as the word the graph stores: an int or a bool as
    /// itself, a real as the bits of its float.
    /// </param>
    public int Variable(string name, HKX2.VariableType type, int initialBits = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        int index = VariableIndex(name);
        if (index >= 0) return index;

        Strings.m_variableNames = Append(Strings.m_variableNames, name);
        Data.m_variableInfos = Append(Data.m_variableInfos, new hkbVariableInfo
        {
            m_type = (sbyte)type,
            m_role = new hkbRoleAttribute { m_role = 0, m_flags = 0 },
        });

        Data.m_variableInitialValues ??= new hkbVariableValueSet();
        Data.m_variableInitialValues.m_wordVariableValues =
            Append(Data.m_variableInitialValues.m_wordVariableValues, new hkbVariableValue { m_value = initialBits });

        return Strings.m_variableNames.Count - 1;
    }

    public int BoolVariable(string name, bool initial = false) => Variable(name, HKX2.VariableType.VARIABLE_TYPE_BOOL, initial ? 1 : 0);

    public int IntVariable(string name, int initial = 0) => Variable(name, HKX2.VariableType.VARIABLE_TYPE_INT32, initial);

    public int RealVariable(string name, float initial = 0f) =>
        Variable(name, HKX2.VariableType.VARIABLE_TYPE_REAL, BitConverter.SingleToInt32Bits(initial));

    /// <summary>The name of an event by index, or null.</summary>
    public string? EventName(int index) =>
        index >= 0 && index < Strings.m_eventNames.Count ? Strings.m_eventNames[index] : null;

    // ------------------------------------------------------------ finding

    /// <summary>Every object the graph reaches from its root, including what was added.</summary>
    public IEnumerable<IHavokObject> Reachable() => Walk(Graph);

    /// <summary>The first node of a type with a name, reached from the root.</summary>
    public T? Find<T>(string name) where T : hkbNode =>
        Reachable().OfType<T>().FirstOrDefault(n => string.Equals(n.m_name, name, StringComparison.Ordinal));

    /// <summary>A node the graph must have.</summary>
    public T Require<T>(string name) where T : hkbNode =>
        Find<T>(name) ?? throw new InvalidDataException($"'{File.Path}' has no {typeof(T).Name} named '{name}'");

    /// <summary>A state of a machine by name.</summary>
    public hkbStateMachineStateInfo? StateOf(hkbStateMachine machine, string name) =>
        machine.m_states.FirstOrDefault(s => string.Equals(s.m_name, name, StringComparison.Ordinal));

    /// <summary>The state machine that owns a state, among those reached from the root.</summary>
    public hkbStateMachine? MachineOf(hkbStateMachineStateInfo state) =>
        Reachable().OfType<hkbStateMachine>().FirstOrDefault(m => m.m_states.Contains(state));

    // ------------------------------------------------------------ generators

    /// <summary>A clip generator over an animation, with the events it fires.</summary>
    /// <param name="animation">The animation as the character stores it, e.g. <c>Animations\WalkForward.hkx</c>.</param>
    /// <param name="triggers">Events at a local time; a time relative to the end is negative-or-zero from the end.</param>
    public hkbClipGenerator Clip(
        string name, string animation, ClipMode mode = ClipMode.SinglePlay, float playbackSpeed = 1f,
        bool mirrored = false, params (string Event, float Time, bool FromEnd)[] triggers)
    {
        var clip = new hkbClipGenerator
        {
            m_name = name,
            m_animationName = animation,
            m_mode = (sbyte)mode,
            m_flags = (sbyte)(mirrored ? 4 : 0),
            m_playbackSpeed = playbackSpeed,
            m_animationBindingIndex = -1,
            m_triggers = triggers.Length == 0 ? null : new hkbClipTriggerArray
            {
                m_triggers = triggers.Select(t => new hkbClipTrigger
                {
                    m_localTime = t.Time,
                    m_relativeToEndOfClip = t.FromEnd,
                    m_event = new hkbEventProperty { m_id = Event(t.Event) },
                }).ToList(),
            },
        };

        return clip;
    }

    /// <summary>Adds an event a clip fires; at its end when <paramref name="fromEnd"/>.</summary>
    public void AddTrigger(hkbClipGenerator clip, string eventName, float time = 0f, bool fromEnd = true)
    {
        clip.m_triggers ??= new hkbClipTriggerArray();
        clip.m_triggers.m_triggers = Append(clip.m_triggers.m_triggers, new hkbClipTrigger
        {
            m_localTime = time,
            m_relativeToEndOfClip = fromEnd,
            m_event = new hkbEventProperty { m_id = Event(eventName) },
        });
    }

    /// <summary>
    /// A parametric blend over a variable: each child is fully weighted where
    /// the variable equals its weight, as the locomotion blends read speed.
    /// </summary>
    public hkbBlenderGenerator Blend(string name, string parameterVariable, params (hkbGenerator Child, float Weight)[] children)
    {
        var blend = new hkbBlenderGenerator
        {
            m_name = name,
            m_flags = SyncParametricBlend,
            m_blendParameter = 1f,
            m_minCyclicBlendParameter = 0f,
            m_maxCyclicBlendParameter = 1f,
            m_indexOfSyncMasterChild = -1,
            m_children = children.Select(c => new hkbBlenderGeneratorChild
            {
                m_generator = c.Child,
                m_weight = c.Weight,
                m_worldFromModelWeight = 1f,
            }).ToList(),
        };

        Bind(blend, "blendParameter", parameterVariable);
        return blend;
    }

    /// <summary>A state machine, starting in a state by id.</summary>
    /// <param name="randomStartEvent">
    /// Set for a machine that starts in a random state and re-picks on this
    /// event, the way the sabre cat's lying-down idles cycle.
    /// </param>
    public hkbStateMachine StateMachine(string name, int startStateId = 0, string? randomStartEvent = null) => new()
    {
        m_name = name,
        m_startStateId = startStateId,
        m_startStateMode = (sbyte)(randomStartEvent is null ? 0 : 2),
        m_randomTransitionEventId = randomStartEvent is null ? -1 : Event(randomStartEvent),
        m_returnToPreviousStateEventId = -1,
        m_transitionToNextHigherStateEventId = -1,
        m_transitionToNextLowerStateEventId = -1,
        m_syncVariableIndex = -1,
        m_maxSimultaneousTransitions = 32,
        m_eventToSendWhenStateOrTransitionChanges = new hkbEvent { m_id = -1 },
        m_states = new List<hkbStateMachineStateInfo>(),
    };

    /// <summary>A generator with a modifier over it.</summary>
    public hkbModifierGenerator Modified(string name, hkbModifier modifier, hkbGenerator generator) => new()
    {
        m_name = name,
        m_userData = 1,
        m_modifier = modifier,
        m_generator = generator,
    };

    /// <summary>A modifier that sends one event every few of another, the idle cyclers' clock.</summary>
    public BSEventEveryNEventsModifier EveryN(
        string name, string eventToCheck, string eventToSend, sbyte every, sbyte atLeast, bool randomize) => new()
    {
        m_name = name,
        m_enable = true,
        m_eventToCheckFor = new hkbEventProperty { m_id = Event(eventToCheck) },
        m_eventToSend = new hkbEventProperty { m_id = Event(eventToSend) },
        m_numberOfEventsBeforeSend = every,
        m_minimumNumberOfEventsBeforeSend = atLeast,
        m_randomizeNumberOfEvents = randomize,
    };

    /// <summary>
    /// An expression modifier. An assignment is evaluated every frame; an
    /// <c>event if (...)</c> sends when its condition turns true, as vanilla's do.
    /// </summary>
    public hkbEvaluateExpressionModifier Expressions(string name, params string[] expressions) => new()
    {
        m_name = name,
        m_enable = true,
        m_userData = 2,
        m_expressions = new hkbExpressionDataArray
        {
            m_expressionsData = expressions.Select(e => new hkbExpressionData
            {
                m_expression = e,
                m_assignmentVariableIndex = -1,
                m_assignmentEventIndex = -1,
                m_eventMode = (sbyte)(e.Contains(" if ", StringComparison.Ordinal) ? 2 : 0),
            }).ToList(),
        },
    };

    /// <summary>A list of modifiers, applied in order.</summary>
    public hkbModifierList ModifierList(string name, params hkbModifier[] modifiers) => new()
    {
        m_name = name,
        m_enable = true,
        m_modifiers = modifiers.ToList(),
    };

    /// <summary>
    /// A blending transition. The duration is bound to a variable when one is
    /// named and exists, which is how vanilla's <c>DefaultBlend</c> reads
    /// <c>blendDefault</c>.
    /// </summary>
    /// <param name="flags">1 ignores the from-generator's world-from-model, 4 the to-generator's, 2 syncs.</param>
    public hkbBlendingTransitionEffect Effect(
        string name, float duration = 0f, ushort flags = 0, sbyte selfTransitionMode = 0, string? durationVariable = null)
    {
        var effect = new hkbBlendingTransitionEffect
        {
            m_name = name,
            m_duration = duration,
            m_flags = flags,
            m_endMode = 0,
            m_blendCurve = 0,
            m_selfTransitionMode = selfTransitionMode,
            m_eventMode = 0,
        };

        if (durationVariable is not null && VariableIndex(durationVariable) >= 0)
            Bind(effect, "duration", durationVariable);

        return effect;
    }

    /// <summary>Binds a member of a node to a variable.</summary>
    public void Bind(hkbBindable node, string memberPath, string variable)
    {
        node.m_variableBindingSet ??= new hkbVariableBindingSet { m_indexOfBindingToEnable = -1 };
        node.m_variableBindingSet.m_bindings = Append(node.m_variableBindingSet.m_bindings, new hkbVariableBindingSetBinding
        {
            m_memberPath = memberPath,
            m_variableIndex = Variable(variable, HKX2.VariableType.VARIABLE_TYPE_REAL),
            m_bitIndex = -1,
            m_bindingType = 0,
        });
    }

    // ------------------------------------------------------------ states and transitions

    /// <summary>Adds a state to a machine, with the next free id unless one is given.</summary>
    public hkbStateMachineStateInfo State(hkbStateMachine machine, string name, hkbGenerator generator, int? id = null)
    {
        ArgumentNullException.ThrowIfNull(machine);

        var state = new hkbStateMachineStateInfo
        {
            m_name = name,
            m_stateId = id ?? (machine.m_states.Count == 0 ? 0 : machine.m_states.Max(s => s.m_stateId) + 1),
            m_generator = generator,
            m_probability = 1f,
            m_enable = true,
        };

        machine.m_states = Append(machine.m_states, state);
        return state;
    }

    /// <summary>A transition out of a state on an event.</summary>
    /// <param name="condition">An expression that must hold, or null.</param>
    /// <param name="toNestedStateId">A state of the target machine to enter, or null for its start state.</param>
    public hkbStateMachineTransitionInfo Transition(
        hkbStateMachineStateInfo from, string eventName, hkbStateMachineStateInfo to,
        hkbTransitionEffect? effect = null, string? condition = null, int? toNestedStateId = null)
    {
        ArgumentNullException.ThrowIfNull(from);

        from.m_transitions ??= new hkbStateMachineTransitionInfoArray();
        hkbStateMachineTransitionInfo transition = NewTransition(eventName, to.m_stateId, effect, condition, toNestedStateId, wildcard: false);
        from.m_transitions.m_transitions = Append(from.m_transitions.m_transitions, transition);
        return transition;
    }

    /// <summary>A transition from any state of a machine on an event.</summary>
    public hkbStateMachineTransitionInfo Wildcard(
        hkbStateMachine machine, string eventName, hkbStateMachineStateInfo to,
        hkbTransitionEffect? effect = null, string? condition = null, int? toNestedStateId = null)
    {
        ArgumentNullException.ThrowIfNull(machine);

        machine.m_wildcardTransitions ??= new hkbStateMachineTransitionInfoArray();
        hkbStateMachineTransitionInfo transition = NewTransition(eventName, to.m_stateId, effect, condition, toNestedStateId, wildcard: true);
        machine.m_wildcardTransitions.m_transitions = Append(machine.m_wildcardTransitions.m_transitions, transition);
        return transition;
    }

    private hkbStateMachineTransitionInfo NewTransition(
        string eventName, int toStateId, hkbTransitionEffect? effect, string? condition, int? toNestedStateId, bool wildcard)
    {
        short flags = 0;
        if (condition is null) flags |= DisableCondition;
        if (wildcard) flags |= LocalWildcard;
        if (toNestedStateId is not null) flags |= ToNestedStateIdIsValid;

        return new hkbStateMachineTransitionInfo
        {
            m_eventId = Event(eventName),
            m_toStateId = toStateId,
            m_toNestedStateId = toNestedStateId ?? 0,
            m_transition = effect,
            m_condition = condition is null ? null : new hkbExpressionCondition { m_expression = condition },
            m_flags = flags,
            m_triggerInterval = Never(),
            m_initiateInterval = Never(),
        };
    }

    private static hkbStateMachineTimeInterval Never() => new()
    {
        m_enterEventId = -1,
        m_exitEventId = -1,
        m_enterTime = 0f,
        m_exitTime = 0f,
    };

    /// <summary>Events sent when a state is entered.</summary>
    public void EnterEvents(hkbStateMachineStateInfo state, params string[] events) =>
        state.m_enterNotifyEvents = EventArray(events);

    /// <summary>Events sent when a state is left.</summary>
    public void ExitEvents(hkbStateMachineStateInfo state, params string[] events) =>
        state.m_exitNotifyEvents = EventArray(events);

    private hkbStateMachineEventPropertyArray? EventArray(string[] events) => events.Length == 0 ? null : new()
    {
        m_events = events.Select(e => new hkbEventProperty { m_id = Event(e) }).ToList(),
    };

    /// <summary>
    /// Removes a state and every transition into it, from its siblings and from
    /// the machine's wildcards. The generator under it goes with it.
    /// </summary>
    public bool RemoveState(hkbStateMachine machine, string name)
    {
        ArgumentNullException.ThrowIfNull(machine);

        if (StateOf(machine, name) is not { } state) return false;

        machine.m_states = machine.m_states.Where(s => !ReferenceEquals(s, state)).ToList();

        foreach (hkbStateMachineStateInfo sibling in machine.m_states)
            if (sibling.m_transitions is { } transitions)
                transitions.m_transitions = transitions.m_transitions.Where(t => t.m_toStateId != state.m_stateId).ToList();

        if (machine.m_wildcardTransitions is { } wildcards)
            wildcards.m_transitions = wildcards.m_transitions.Where(t => t.m_toStateId != state.m_stateId).ToList();

        if (machine.m_startStateId == state.m_stateId && machine.m_states.Count > 0)
            machine.m_startStateId = machine.m_states[0].m_stateId;

        return true;
    }

    /// <summary>Removes the transitions of a state that an event fires.</summary>
    public int RemoveTransitions(hkbStateMachineStateInfo state, string eventName)
    {
        if (state.m_transitions is not { } transitions) return 0;

        int id = EventIndex(eventName);
        int before = transitions.m_transitions.Count;
        transitions.m_transitions = transitions.m_transitions.Where(t => t.m_eventId != id).ToList();
        return before - transitions.m_transitions.Count;
    }

    // ------------------------------------------------------------ helpers

    private static int IndexOf(IList<string> names, string name)
    {
        for (int i = 0; i < names.Count; i++)
            if (string.Equals(names[i], name, StringComparison.Ordinal)) return i;

        return -1;
    }

    /// <summary>
    /// A list with one more item. HKX2 hands back arrays for empty members, which
    /// cannot grow, so the member is replaced rather than appended to.
    /// </summary>
    private static IList<T> Append<T>(IList<T> list, T item)
    {
        if (list is List<T> growable)
        {
            growable.Add(item);
            return growable;
        }

        var copy = new List<T>(list) { item };
        return copy;
    }

    private static IEnumerable<IHavokObject> Walk(IHavokObject root)
    {
        var seen = new HashSet<IHavokObject>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<object>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            object current = stack.Pop();

            switch (current)
            {
                case IHavokObject obj:
                    if (!seen.Add(obj)) continue;
                    yield return obj;

                    foreach (PropertyInfo property in obj.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                        if (property.GetIndexParameters().Length == 0 && !property.PropertyType.IsValueType && property.PropertyType != typeof(string)
                            && property.GetValue(obj) is { } value)
                            stack.Push(value);

                    foreach (FieldInfo field in obj.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                        if (field.GetValue(obj) is { } value)
                            stack.Push(value);
                    break;

                case string:
                    break;

                case System.Collections.IEnumerable items:
                    foreach (object? item in items)
                        if (item is not null) stack.Push(item);
                    break;
            }
        }
    }
}
