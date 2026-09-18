using System.Text.RegularExpressions;
using HKSK.Behavior;
using HKSK.Engine;
using HKSK.Havok;
using HKX2;

namespace HKSK.SetData;

/// <summary>
/// A project's whole behaviour, read for the set data: its nodes across files, what each
/// transition is triggered by, and the choices the weapon in hand makes.
/// </summary>
/// <remarks>
/// <para>
/// Hand types reach the graph three ways, all resolved against <see cref="SetHands"/>: a
/// selector's <c>selectedGeneratorIndex</c> bound to one, a machine's <c>startStateId</c>
/// bound to one, and a transition condition that reads nothing but hand types. Every other
/// choice -- a blend's arms, a selector bound to anything else -- is left open, because the
/// set data lists what <em>can</em> play.
/// </para>
/// <para>
/// <strong>Nodes are compared by identity throughout.</strong> HKX2's objects compare by
/// value, and a behaviour is a cyclic graph: hashing a state machine by value walks most of
/// the behaviour below it, on every lookup. A set keyed that way made one event's walk take
/// seconds.
/// </para>
/// </remarks>
internal sealed class GraphReach
{
    /// <summary>The two variables the game passes when it selects a set by weapon.</summary>
    public const string Right = "iRightHandType", Left = "iLeftHandType";

    private const int FlagDisabled = 0x20, FlagToNestedStateIdIsValid = 0x2000;

    private static readonly Regex Identifier = new(@"[A-Za-z_]\w*", RegexOptions.Compiled);

    private readonly string _folder;
    private readonly Dictionary<IHavokObject, string> _fileOf = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, Variables> _tables = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, hkbGenerator> _fileRoot = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<hkbStateMachine> _machines = [];
    private Dictionary<string, List<hkbStateMachine>>? _byEvent;
    private HashSet<IHavokObject>? _reachable;
    private Dictionary<IHavokObject, List<IHavokObject>>? _parents;

    private GraphReach(string projectFile)
    {
        _folder = Path.GetDirectoryName(projectFile)!;
        var space = new VariableSpace();

        foreach (ProjectStep step in ProjectWalk.Of(projectFile).Steps)
        {
            _fileOf.TryAdd(step.Node, step.File);

            if (step.Node is hkbStateMachine machine) _machines.Add(machine);
            if (step.Node is not hkbBehaviorGraph graph) continue;

            if (!_tables.ContainsKey(step.File)) _tables[step.File] = Variables.Of(graph, space);
            if (graph.m_rootGenerator is null) continue;

            _fileRoot.TryAdd(step.File, graph.m_rootGenerator);
            Root ??= graph.m_rootGenerator;
        }
    }

    /// <summary>Reads a project's whole behaviour, or null when it has none.</summary>
    public static GraphReach? Of(string projectFile)
    {
        var reach = new GraphReach(projectFile);
        return reach.Root is null ? null : reach;
    }

    /// <summary>The root generator of the file the character names.</summary>
    public hkbGenerator? Root { get; }

    /// <summary>Whether anything in the graph chooses by hand type at all.</summary>
    public bool ChoosesByHand =>
        _fileOf.Keys.OfType<hkbNode>().Any(n => HandBinding(n, "selectedGeneratorIndex") is not null
                                              || HandBinding(n, "startStateId") is not null);

    /// <summary>The hand types the choices resolve against from now on.</summary>
    public void SetHands(int right, int left)
    {
        _reachable = null;

        foreach (Variables table in _tables.Values)
        {
            table.Set(Right, right);
            table.Set(Left, left);
        }
    }

    /// <summary>
    /// The clips an attack event plays: the state it enters, narrowed by the nested state
    /// the transition names or the event's own transitions below, else the start state.
    /// </summary>
    /// <remarks>
    /// Narrower than everything an event leads to (<see cref="StateGraph.UntilHome"/>), because the race
    /// reads it as the clips of <em>that</em> attack: the chaurus's eleven attacks enter
    /// one state, and the nested state each transition names is what tells them apart.
    /// </remarks>
    public List<hkbClipGenerator> ClipsOfAttack(string eventName)
    {
        var clips = new List<hkbClipGenerator>();
        var seen = new HashSet<hkbClipGenerator>(ReferenceEqualityComparer.Instance);

        foreach ((hkbStateMachine machine, hkbStateMachineTransitionInfo info) in TransitionsOn(eventName))
            if (StateOf(machine, info.m_toStateId)?.m_generator is { } target)
                foreach (hkbClipGenerator clip in Enter(target, eventName, NestedOf(info)))
                    if (seen.Add(clip)) clips.Add(clip);

        return clips;
    }

    /// <summary>The children a walk follows: across references, and down the branch a hand type picks.</summary>
    internal IEnumerable<IHavokObject> Children(IHavokObject node)
    {
        if (node is hkbBehaviorReferenceGenerator reference)
        {
            string? path = HavokPath.Resolve(_folder, reference.m_behaviorName);
            if (path is not null && _fileRoot.TryGetValue(Path.GetFullPath(path), out hkbGenerator? root))
                yield return root;
            yield break;
        }

        if (node is hkbManualSelectorGenerator selector && HandBinding(selector, "selectedGeneratorIndex") is { } index)
        {
            if (index >= 0 && index < selector.m_generators.Count && selector.m_generators[index] is { } chosen)
                yield return chosen;
            yield break;
        }

        foreach ((_, _, IHavokObject child) in HavokEdges.Of(node)) yield return child;
    }

    /// <summary>The value a member is bound to, when the variable bound is a hand type.</summary>
    internal int? HandBinding(hkbNode node, string member)
    {
        if (node.m_variableBindingSet is not { } bindings) return null;
        if (!_fileOf.TryGetValue(node, out string? file) || !_tables.TryGetValue(file, out Variables? table)) return null;

        foreach (hkbVariableBindingSetBinding binding in bindings.m_bindings)
        {
            if (binding.m_memberPath != member) continue;
            if (binding.m_variableIndex < 0 || binding.m_variableIndex >= table.Count) continue;
            if (table.NameOf(binding.m_variableIndex) is not (Right or Left)) continue;

            return table.AsInt(binding.m_variableIndex);
        }

        return null;
    }

    /// <summary>Whether a transition's condition allows it under the hand types set.</summary>
    /// <remarks>
    /// Decided only when the condition reads nothing but hand types. Anything else it reads
    /// is a situation the character may be in, so the transition is kept.
    /// </remarks>
    internal bool HandConditionHolds(hkbStateMachine machine, hkbCondition? condition)
    {
        if (condition is not hkbExpressionCondition expression) return true;
        if (!_fileOf.TryGetValue(machine, out string? file) || !_tables.TryGetValue(file, out Variables? table)) return true;

        var names = Identifier.Matches(expression.m_expression).Select(m => m.Value).ToList();
        if (names.Count == 0 || !names.All(n => n is Right or Left)) return true;

        return Transitions.Holds(expression, table);
    }

    /// <summary>The machines nearest below a generator, not looking into them.</summary>
    internal List<hkbStateMachine> NestedMachines(hkbGenerator? generator)
    {
        var machines = new List<hkbStateMachine>();
        if (generator is null) return machines;

        var seen = new HashSet<IHavokObject>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<IHavokObject>();
        stack.Push(generator);

        while (stack.Count > 0)
        {
            IHavokObject node = stack.Pop();
            if (!seen.Add(node)) continue;
            if (node is hkbStateMachine machine) { machines.Add(machine); continue; }

            foreach (IHavokObject child in Children(node)) stack.Push(child);
        }

        return machines;
    }

    /// <summary>The name an event id has in the file a machine lives in.</summary>
    internal string? EventName(hkbStateMachine machine, int id) =>
        _fileOf.TryGetValue(machine, out string? file) && _tables.TryGetValue(file, out Variables? table)
            ? table.EventNameOf(id)
            : null;

    internal static hkbStateMachineStateInfo? StateOf(hkbStateMachine machine, int id) =>
        machine.m_states.FirstOrDefault(s => s.m_stateId == id);

    private static int NestedOf(hkbStateMachineTransitionInfo info) =>
        (info.m_flags & FlagToNestedStateIdIsValid) != 0 ? info.m_toNestedStateId : -1;

    private IEnumerable<(hkbStateMachine Machine, hkbStateMachineTransitionInfo Info)> TransitionsOn(string eventName)
    {
        foreach (hkbStateMachine machine in MachinesOn(eventName))
        {
            _reachable ??= new HashSet<IHavokObject>(Reachable(Root!), ReferenceEqualityComparer.Instance);
            if (!_reachable.Contains(machine)) continue;

            // a wildcard is offered once per state it leaves; one is enough
            var once = new HashSet<hkbStateMachineTransitionInfo>(ReferenceEqualityComparer.Instance);
            foreach (hkbStateMachineTransitionInfo info in On(machine, eventName))
                if (once.Add(info) && StateOf(machine, info.m_toStateId) is not null)
                    yield return (machine, info);
        }
    }

    // The machines with any transition on an event, whatever state it leaves.
    private List<hkbStateMachine> MachinesOn(string eventName)
    {
        if (_byEvent is null)
        {
            _byEvent = new Dictionary<string, List<hkbStateMachine>>(StringComparer.OrdinalIgnoreCase);

            foreach (hkbStateMachine machine in _machines)
            {
                var infos = (machine.m_wildcardTransitions?.m_transitions ?? [])
                    .Concat(machine.m_states.SelectMany(s => s.m_transitions?.m_transitions ?? []));

                foreach (string name in infos.Select(i => EventName(machine, i.m_eventId)).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (!_byEvent.TryGetValue(name, out var list)) _byEvent[name] = list = [];
                    list.Add(machine);
                }
            }
        }

        return _byEvent.GetValueOrDefault(eventName) ?? [];
    }

    // The transitions an event takes out of each state: the state's own if it has any
    // for the event, the machine's wildcards otherwise, as the runtime checks them.
    private IEnumerable<hkbStateMachineTransitionInfo> On(hkbStateMachine machine, string eventName)
    {
        IList<hkbStateMachineTransitionInfo> wild = machine.m_wildcardTransitions?.m_transitions ?? [];

        foreach (hkbStateMachineStateInfo state in machine.m_states)
        {
            bool any = false;
            foreach (hkbStateMachineTransitionInfo info in state.m_transitions?.m_transitions ?? [])
                if (Takes(machine, info, eventName, state.m_stateId)) { any = true; yield return info; }

            if (any) continue;

            foreach (hkbStateMachineTransitionInfo info in wild)
                if (Takes(machine, info, eventName, state.m_stateId)) yield return info;
        }
    }

    private bool Takes(hkbStateMachine machine, hkbStateMachineTransitionInfo info, string eventName, int from) =>
        (info.m_flags & FlagDisabled) == 0 && info.m_toStateId != from
        && string.Equals(EventName(machine, info.m_eventId), eventName, StringComparison.OrdinalIgnoreCase)
        && HandConditionHolds(machine, info.m_condition);

    private HashSet<hkbClipGenerator> Enter(hkbGenerator start, string eventName, int nested)
    {
        var clips = new HashSet<hkbClipGenerator>(ReferenceEqualityComparer.Instance);
        var seen = new HashSet<IHavokObject>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<IHavokObject>();
        stack.Push(start);

        while (stack.Count > 0)
        {
            IHavokObject node = stack.Pop();
            if (!seen.Add(node)) continue;

            if (node is hkbClipGenerator clip) clips.Add(clip);

            if (node is not hkbStateMachine machine)
            {
                foreach (IHavokObject child in Children(node)) stack.Push(child);
                continue;
            }

            var entry = new List<int>();
            if (nested >= 0 && StateOf(machine, nested) is not null) entry.Add(nested);
            if (entry.Count == 0)
                entry.AddRange(On(machine, eventName).Select(t => t.m_toStateId).Where(id => StateOf(machine, id) is not null));
            if (entry.Count == 0) entry.Add(HandBinding(machine, "startStateId") ?? machine.m_startStateId);

            foreach (int id in entry)
                if (StateOf(machine, id)?.m_generator is { } generator) stack.Push(generator);

            nested = -1;
        }

        return clips;
    }

    /// <summary>
    /// Whether an attack's travel is chosen by how fast the character is moving, which means
    /// the attack has no root motion of its own to measure.
    /// </summary>
    /// <remarks>
    /// <para>
    /// True when a blender above any of the clips is <em>parametric on speed</em>: its
    /// <c>blendParameter</c> is bound to a variable named for speed, and its arms sit at
    /// positions along that axis in units per second. The player's
    /// <c>1HM_Forward_AttackLeft_Blend</c> puts the standing swing at 25, the walking one at 82
    /// and the running one at 232, on <c>SpeedDamped</c>; the werewolf's
    /// <c>LeftAttackForwardBlend</c> puts standing at 50 and its running directional blend at
    /// 325, on <c>SampledSpeed</c>. So the clip that plays -- and how far it carries the
    /// character -- is interpolated at runtime, and the single reach the set data can state for
    /// the attack has no correct value. That is what the moving-attack flag is for: it tells
    /// combat to measure the attack from the same speed the graph blends by
    /// (<c>docs/animation-set-data.md</c> §4.6).
    /// </para>
    /// <para>
    /// Distance does not enter it: of the 14 attacks in the shipped game this holds for, the
    /// blender sits two levels above the clip in eleven and four in the rest. <strong>Every
    /// parent is followed</strong>, because a clip generator is often shared and a walk records
    /// only the first parent it happened to reach.
    /// </para>
    /// <para>
    /// A <strong>sprint attack</strong> is the same situation with the blend missing. Sprinting
    /// is single-direction locomotion, so there is nothing to interpolate and the attack is one
    /// clip in a state -- but the character is still carried at sprint speed, and the graph says
    /// so by raising <c>IsSprinting</c> over that state. So the answer is also yes when a node
    /// on the way up, or a modifier one of them attaches, binds that variable. Against the
    /// shipped file the two together are right about 25 of the 34 attacks whose clips can be
    /// resolved, with four disagreements: the Vampire Lord's two, whose blend is the player's
    /// pattern in a project that flags nothing, and the werewolf's
    /// <c>AttackStartLeftSprinting</c> and <c>AttackStartRightSprinting</c>, which vanilla
    /// leaves clear while flagging <c>AttackStartDualSprinting</c> beside them. What is left
    /// undecided is the hovering creatures (§6).
    /// </para>
    /// </remarks>
    public bool TravelChosenBySpeed(IEnumerable<hkbClipGenerator> clips)
    {
        ArgumentNullException.ThrowIfNull(clips);
        _parents ??= Parents();

        var seen = new HashSet<IHavokObject>(ReferenceEqualityComparer.Instance);
        var queue = new Queue<IHavokObject>();
        foreach (hkbClipGenerator clip in clips)
            if (seen.Add(clip)) queue.Enqueue(clip);

        while (queue.Count > 0)
            foreach (IHavokObject up in _parents.GetValueOrDefault(queue.Dequeue()) ?? [])
            {
                if (!seen.Add(up)) continue;
                if (up is hkbBlenderGenerator blender && ParametricOnSpeed(blender)) return true;
                if (up is hkbNode node && Writes(node, Sprinting)) return true;
                if (up is hkbModifierGenerator { m_modifier: { } modifier } && Attaches(modifier, Sprinting)) return true;
                queue.Enqueue(up);
            }

        return false;
    }

    /// <summary>The variable a graph raises while the character sprints.</summary>
    private const string Sprinting = "IsSprinting";

    /// <summary>Whether a node binds a member to the named variable, either way round.</summary>
    private bool Writes(hkbNode node, string variable)
    {
        if (node.m_variableBindingSet is not { } bindings) return false;
        if (!_tables.TryGetValue(_fileOf.GetValueOrDefault(node, ""), out Variables? table)) return false;

        foreach (hkbVariableBindingSetBinding binding in bindings.m_bindings)
            if (binding.m_variableIndex >= 0
                && table.NameOf(binding.m_variableIndex) is { } name
                && string.Equals(name, variable, StringComparison.OrdinalIgnoreCase))
                return true;

        return false;
    }

    /// <summary>Whether the modifiers a generator attaches touch the named variable.</summary>
    private bool Attaches(IHavokObject modifier, string variable)
    {
        var seen = new HashSet<IHavokObject>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<IHavokObject>();
        stack.Push(modifier);

        while (stack.Count > 0)
        {
            IHavokObject at = stack.Pop();
            if (!seen.Add(at)) continue;
            if (at is hkbGenerator && !ReferenceEquals(at, modifier)) continue;  // not down another branch
            if (at is hkbNode node && Writes(node, variable)) return true;

            foreach ((_, _, IHavokObject child) in HavokEdges.Of(at)) stack.Push(child);
        }

        return false;
    }

    private bool ParametricOnSpeed(hkbBlenderGenerator blender)
    {
        if (blender.m_variableBindingSet is not { } bindings) return false;
        if (!_tables.TryGetValue(_fileOf.GetValueOrDefault(blender, ""), out Variables? table)) return false;

        foreach (hkbVariableBindingSetBinding binding in bindings.m_bindings)
            if (binding.m_memberPath == "blendParameter"
                && binding.m_variableIndex >= 0
                && table.NameOf(binding.m_variableIndex) is { } name
                && name.Contains("speed", StringComparison.OrdinalIgnoreCase))
                return true;

        return false;
    }

    /// <summary>Every parent of every node the root reaches, all of them, not the first seen.</summary>
    private Dictionary<IHavokObject, List<IHavokObject>> Parents()
    {
        var parents = new Dictionary<IHavokObject, List<IHavokObject>>(ReferenceEqualityComparer.Instance);

        foreach (IHavokObject node in Reachable(Root!))
            foreach (IHavokObject child in Children(node))
            {
                if (!parents.TryGetValue(child, out List<IHavokObject>? list)) parents[child] = list = [];
                if (!list.Any(x => ReferenceEquals(x, node))) list.Add(node);
            }

        return parents;
    }

    private IEnumerable<IHavokObject> Reachable(hkbGenerator root)
    {
        var seen = new HashSet<IHavokObject>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<IHavokObject>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            IHavokObject node = stack.Pop();
            if (!seen.Add(node)) continue;

            yield return node;
            foreach (IHavokObject child in Children(node)) stack.Push(child);
        }
    }
}
