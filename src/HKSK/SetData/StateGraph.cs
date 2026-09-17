using System.Runtime.CompilerServices;
using HKSK.Havok;
using HKX2;

namespace HKSK.SetData;

/// <summary>
/// A behaviour as a graph of states: which state can follow which, and which clips each
/// one plays itself.
/// </summary>
/// <remarks>
/// <para>
/// A behaviour is a graph, not a tree, and a cyclic one: an idle's exit leads back to the
/// default state, from which every other idle is entered. What an event leads to is
/// therefore bounded by <em>home</em> -- the states the graph reaches with no key at all.
/// From the states an event enters, every transition is followed, on a key or not, until
/// the graph is home again (<see cref="UntilHome"/>). That brings in an idle's loop, the
/// variants its next-clip key picks between, and its exit, and stops before the default
/// state every exit returns to. <c>docs/animation-set-data.md</c> §5.4 compares it with
/// the rules that were measured and dropped.
/// </para>
/// <para>
/// A node is a state of a machine. A state's own clips are the clips below its generator
/// down to, not into, the machines nested there. Edges are a nested machine's entry --
/// its start state, or every state when it starts at random or from a sync variable --
/// a state's own transitions, a machine's wildcards and its random-transition event from
/// every state, and the nested state a transition names. Weapon choices are resolved
/// against the hand types, as <see cref="GraphReach"/> does.
/// </para>
/// </remarks>
internal sealed class StateGraph
{
    private const int FlagDisabled = 0x20, FlagToNestedStateIdIsValid = 0x2000;

    /// <summary><c>hkbStateMachine::StartStateMode</c>: from a sync variable, or at random.</summary>
    private const sbyte StartSync = 1, StartRandom = 2;

    /// <summary>The node every walk starts from: the root generator, above its first machine.</summary>
    private const int Root = 0;

    private readonly List<HashSet<hkbClipGenerator>> _clips = [];
    private readonly List<List<int>> _next = [];
    private readonly Dictionary<string, List<int>> _entered = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<int> _home = [];

    private StateGraph() { }

    /// <summary>
    /// Everything an event can lead to before the graph is home again: every transition,
    /// on a key or not, followed from the states it enters, stopping at home states and at
    /// the doors of other idles.
    /// </summary>
    public HashSet<int> UntilHome(string eventName)
    {
        var reached = new HashSet<int>();
        if (!_entered.TryGetValue(eventName, out List<int>? targets)) return reached;

        var queue = new Queue<int>();
        foreach (int target in targets)
            if (!_home.Contains(target) && reached.Add(target)) queue.Enqueue(target);

        // another idle's door is where another set begins
        while (queue.Count > 0)
            foreach (int next in _next[queue.Dequeue()])
                if (!_home.Contains(next) && !_doors.Contains(next) && reached.Add(next)) queue.Enqueue(next);

        return reached;
    }

    /// <summary>
    /// States a key enters straight from home: where an idle begins. A chair's entry is a
    /// door; its exit and its next clip, entered from inside the chair, are not.
    /// </summary>
    private readonly HashSet<int> _doors = [];
    private readonly List<(int From, int To)> _keyedEdges = [];

    /// <summary>Every state the root can reach.</summary>
    public HashSet<int> Everything()
    {
        var reached = new HashSet<int> { Root };
        var queue = new Queue<int>([Root]);

        while (queue.Count > 0)
            foreach (int next in _next[queue.Dequeue()])
                if (reached.Add(next)) queue.Enqueue(next);

        return reached;
    }

    /// <summary>The states reached from the root through nesting and transitions on no key.</summary>
    public IReadOnlySet<int> Home => _home;

    /// <summary>Whether any reachable machine has a transition on an event.</summary>
    public bool Handles(string eventName) => _entered.ContainsKey(eventName);

    /// <summary>The clips a set of states play themselves.</summary>
    public IEnumerable<hkbClipGenerator> ClipsOf(IEnumerable<int> states) => states.SelectMany(s => _clips[s]);

    /// <summary>A value two graphs share when every choice resolved to the same edges.</summary>
    public string Signature { get; private set; } = "";

    /// <summary>Builds the graph a project's behaviour has for one pair of hand types.</summary>
    public static StateGraph Build(GraphReach behaviour, int right, int left, IReadOnlySet<string> keys)
    {
        behaviour.SetHands(right, left);

        var graph = new StateGraph();
        var ids = new Dictionary<(hkbStateMachine, int), int>(StateKey.Instance);
        var machines = new List<hkbStateMachine>();
        var known = new HashSet<hkbStateMachine>(ReferenceEqualityComparer.Instance);
        var free = new HashSet<(int, int)>();

        int Node()
        {
            graph._clips.Add(new HashSet<hkbClipGenerator>(ReferenceEqualityComparer.Instance));
            graph._next.Add([]);
            return graph._clips.Count - 1;
        }

        int StateNode(hkbStateMachine machine, int state)
        {
            if (ids.TryGetValue((machine, state), out int id)) return id;
            id = Node();
            ids[(machine, state)] = id;
            return id;
        }

        // a node's own clips, and the machines nested below it
        List<hkbStateMachine> Own(int node, hkbGenerator? generator)
        {
            var nested = new List<hkbStateMachine>();
            if (generator is null) return nested;

            var seen = new HashSet<IHavokObject>(ReferenceEqualityComparer.Instance);
            var stack = new Stack<IHavokObject>();
            stack.Push(generator);

            while (stack.Count > 0)
            {
                IHavokObject item = stack.Pop();
                if (!seen.Add(item)) continue;

                if (item is hkbStateMachine machine) { nested.Add(machine); continue; }
                if (item is hkbClipGenerator clip) graph._clips[node].Add(clip);

                foreach (IHavokObject child in behaviour.Children(item)) stack.Push(child);
            }

            return nested;
        }

        // A machine is entered at its start state -- or, when its start state mode is sync
        // (1, from a variable) or random (2), at any of them.
        void Enter(int from, hkbStateMachine machine)
        {
            IEnumerable<int> starts = machine.m_startStateMode is StartSync or StartRandom
                ? machine.m_states.Select(s => s.m_stateId)
                : [behaviour.HandBinding(machine, "startStateId") ?? machine.m_startStateId];

            foreach (int start in starts)
            {
                if (GraphReach.StateOf(machine, start) is null) continue;
                int to = StateNode(machine, start);
                graph._next[from].Add(to);
                free.Add((from, to));
            }

            if (known.Add(machine)) machines.Add(machine);
        }

        Node(); // the root
        foreach (hkbStateMachine machine in Own(Root, behaviour.Root)) Enter(Root, machine);

        for (int m = 0; m < machines.Count; m++)
        {
            hkbStateMachine machine = machines[m];
            IList<hkbStateMachineTransitionInfo> wild = machine.m_wildcardTransitions?.m_transitions ?? [];

            foreach (hkbStateMachineStateInfo state in machine.m_states)
            {
                int node = StateNode(machine, state.m_stateId);
                var nested = Own(node, state.m_generator);
                foreach (hkbStateMachine inner in nested) Enter(node, inner);

                // a machine's random-transition event takes any state to any other
                if (behaviour.EventName(machine, machine.m_randomTransitionEventId) is { } random)
                {
                    bool key = keys.Contains(random);
                    if (!graph._entered.TryGetValue(random, out var entered)) graph._entered[random] = entered = [];

                    foreach (hkbStateMachineStateInfo other in machine.m_states)
                    {
                        if (other.m_stateId == state.m_stateId) continue;
                        int to = StateNode(machine, other.m_stateId);
                        graph._next[node].Add(to);
                        if (!key) free.Add((node, to));
                        else graph._keyedEdges.Add((node, to));
                        if (!entered.Contains(to)) entered.Add(to);
                    }
                }

                IEnumerable<hkbStateMachineTransitionInfo> leaving = (state.m_transitions?.m_transitions ?? []).Concat(wild);
                foreach (hkbStateMachineTransitionInfo info in leaving)
                {
                    if ((info.m_flags & FlagDisabled) != 0 || info.m_toStateId == state.m_stateId) continue;
                    if (StateOf(machine, info.m_toStateId) is not { } target) continue;
                    if (!behaviour.HandConditionHolds(machine, info.m_condition)) continue;

                    int to = StateNode(machine, info.m_toStateId);
                    var targets = new List<int> { to };

                    if ((info.m_flags & FlagToNestedStateIdIsValid) != 0)
                        foreach (hkbStateMachine inner in behaviour.NestedMachines(target.m_generator))
                            if (StateOf(inner, info.m_toNestedStateId) is not null)
                            {
                                targets.Add(StateNode(inner, info.m_toNestedStateId));
                                if (known.Add(inner)) machines.Add(inner);
                                break;
                            }

                    string? name = behaviour.EventName(machine, info.m_eventId);
                    bool key = name is not null && keys.Contains(name);

                    foreach (int t in targets)
                    {
                        graph._next[node].Add(t);
                        if (!key) free.Add((node, t));
                        else graph._keyedEdges.Add((node, t));
                    }

                    if (name is null) continue;
                    if (!graph._entered.TryGetValue(name, out var entered)) graph._entered[name] = entered = [];

                    // Through a nested state, the event is known by that state alone: the
                    // container above it is entered by every event naming one of its states.
                    int entry = targets[^1];
                    if (!entered.Contains(entry)) entered.Add(entry);
                }
            }
        }

        // what the graph reaches without any key: nesting, and transitions on anything else
        graph._home.Add(Root);
        var queue = new Queue<int>([Root]);
        while (queue.Count > 0)
        {
            int at = queue.Dequeue();
            foreach (int next in graph._next[at])
                if (free.Contains((at, next)) && graph._home.Add(next)) queue.Enqueue(next);
        }

        foreach ((int from, int to) in graph._keyedEdges)
            if (graph._home.Contains(from) && !graph._home.Contains(to)) graph._doors.Add(to);

        graph.Signature = string.Join(";", graph._next.Select(n => string.Join(",", n))) + "#" +
                          string.Join(";", graph._clips.Select(c => string.Join(",", c.Select(RuntimeHelpers.GetHashCode).Order())));
        return graph;
    }

    private static hkbStateMachineStateInfo? StateOf(hkbStateMachine machine, int id) =>
        machine.m_states.FirstOrDefault(s => s.m_stateId == id);

    private sealed class StateKey : IEqualityComparer<(hkbStateMachine, int)>
    {
        public static readonly StateKey Instance = new();

        public bool Equals((hkbStateMachine, int) a, (hkbStateMachine, int) b) =>
            ReferenceEquals(a.Item1, b.Item1) && a.Item2 == b.Item2;

        public int GetHashCode((hkbStateMachine, int) key) =>
            HashCode.Combine(RuntimeHelpers.GetHashCode(key.Item1), key.Item2);
    }
}
