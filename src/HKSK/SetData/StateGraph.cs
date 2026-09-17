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
/// default state, from which every other idle is entered. So "what an event leads to" has
/// no answer as a walk -- a walk that follows transitions reaches the whole behaviour, and
/// one that stops at the transitions the game keys on stops before the exit. The answer is
/// <em>dominance</em>. An event's region is the states that cannot be reached from the root
/// without going through the states the event enters: an idle's loop and exit are there,
/// and locomotion, reachable by more than one key, is not.
/// </para>
/// <para>
/// A node is a state of a machine. A state's own clips are the clips below its generator
/// down to, not into, the machines nested there; each nested machine is an edge to its
/// start state. Transitions are edges -- a state's own, and a machine's wildcards from every
/// state -- and a transition naming a nested state is an edge to that state as well.
/// Weapon choices are resolved against the hand types, as <see cref="GraphReach"/> does.
/// </para>
/// </remarks>
internal sealed class StateGraph
{
    private const int FlagDisabled = 0x20, FlagToNestedStateIdIsValid = 0x2000;

    private readonly List<HashSet<hkbClipGenerator>> _clips = [];
    private readonly List<List<int>> _next = [];
    private readonly Dictionary<string, List<int>> _entered = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<int> _unkeyed = [];

    /// <summary>The node every walk starts from: the root generator, above its first machine.</summary>
    private const int Root = 0;

    private StateGraph() { }

    /// <summary>The states that can only be reached through the states an event enters.</summary>
    public HashSet<int> Region(string eventName)
    {
        if (!_entered.TryGetValue(eventName, out List<int>? targets)) return [];

        HashSet<int> blocked = [.. targets];
        HashSet<int> elsewhere = Reach(Root, blocked);
        HashSet<int> region = [];

        foreach (int target in targets)
            foreach (int state in Reach(target, elsewhere))
                region.Add(state);

        return region;
    }

    /// <summary>Every state the root can reach.</summary>
    public HashSet<int> Everything() => Reach(Root, new HashSet<int>());

    /// <summary>Whether any reachable machine has a transition on an event.</summary>
    public bool Handles(string eventName) => _entered.ContainsKey(eventName);

    /// <summary>The clips a set of states play themselves.</summary>
    public IEnumerable<hkbClipGenerator> ClipsOf(IEnumerable<int> states) => states.SelectMany(s => _clips[s]);

    /// <summary>The states reached from the root through transitions on no key at all.</summary>
    public IReadOnlySet<int> Unkeyed => _unkeyed;

    /// <summary>A value two graphs share when every choice resolved to the same edges.</summary>
    public string Signature { get; private set; } = "";

    private HashSet<int> Reach(int from, IReadOnlySet<int> stop)
    {
        var reached = new HashSet<int>();
        var queue = new Queue<int>();
        if (stop.Contains(from) && from != Root) return reached;

        reached.Add(from);
        queue.Enqueue(from);

        while (queue.Count > 0)
            foreach (int next in _next[queue.Dequeue()])
                if (!stop.Contains(next) && reached.Add(next)) queue.Enqueue(next);

        return reached;
    }

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

        void Enter(int from, hkbStateMachine machine)
        {
            int start = behaviour.HandBinding(machine, "startStateId") ?? machine.m_startStateId;
            if (StateOf(machine, start) is not null)
            {
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
                    }

                    if (name is null) continue;
                    if (!graph._entered.TryGetValue(name, out var entered)) graph._entered[name] = entered = [];

                    // Through a nested state, the event is known by that state alone: the
                    // container above it is entered by every event naming one of its
                    // states, and blocking it would give each of them all of the others.
                    int known2 = targets[^1];
                    if (!entered.Contains(known2)) entered.Add(known2);
                }
            }
        }

        // what the graph reaches without any key: nesting, and transitions on anything else
        graph._unkeyed.Add(Root);
        var queue = new Queue<int>([Root]);
        while (queue.Count > 0)
        {
            int at = queue.Dequeue();
            foreach (int next in graph._next[at])
                if (free.Contains((at, next)) && graph._unkeyed.Add(next)) queue.Enqueue(next);
        }

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
