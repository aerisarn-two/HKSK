using System.Text.RegularExpressions;
using HKX2;

namespace HKSK.Behavior;

/// <summary>
/// The values a graph can put <c>iState</c> at, and by what.
/// </summary>
/// <remarks>
/// <para>
/// The engine never writes <c>iState</c>. It reads it: at graph load it scans the
/// variable names for the <c>iState_</c> prefix and keeps each suffix by the variable's
/// initial value, and whenever the movement type may have changed it reads
/// <c>iState</c> back, turns the value into that suffix and applies the movement type
/// of that name (<c>docs/speed-data.md</c> §4.5). The speed sampler keys its table on the
/// same value. So a key is live exactly when the graph can hold it, and the graph holds
/// it by four means: the variable's initial value, a <c>BSiStateTaggingGenerator</c>
/// tagging the subtree it guards, a row of a <c>BSIStateManagerModifier</c> naming a
/// (machine, state) pair, and an expression assigning it.
/// </para>
/// <para>
/// An expression's right-hand side is a constant, a <c>cond</c> choosing between
/// constants, or a constant plus a variable: the deer's
/// <c>iState = iState_DeerDefault + iMovementSpeed</c>. For the last the variable's
/// range is read from the literals of the expressions that assign it --
/// <c>iMovementSpeed = cond((Speed &lt; 100), 0, 1)</c> gives 0 and 1 -- and taken as
/// 0 and 1 where nothing in the project assigns it.
/// </para>
/// </remarks>
public static class StateKeys
{
    /// <summary>How a key is written; one node may write several keys.</summary>
    public enum By { Initial, Tag, Manager, Expression }

    /// <summary>One way a key is written, and the node that writes it.</summary>
    /// <remarks>
    /// <paramref name="Node"/> is the tagging generator, the state machine a manager
    /// row names, or the expression modifier; null for the initial value, which no
    /// node writes.
    /// </remarks>
    public readonly record struct Writer(int Key, By By, IHavokObject? Node);

    private static readonly Regex Literal = new(@"-?\d+", RegexOptions.Compiled);

    /// <summary>
    /// Every value <c>iState</c> can take in the project, with the ways it gets there.
    /// </summary>
    public static IReadOnlyDictionary<int, IReadOnlySet<By>> Writable(ProjectWalk walk, BehaviorRoot root)
    {
        var keys = new SortedDictionary<int, HashSet<By>>();
        foreach (Writer writer in Writers(walk, root))
        {
            if (!keys.TryGetValue(writer.Key, out HashSet<By>? set)) keys[writer.Key] = set = [];
            set.Add(writer.By);
        }

        return keys.ToDictionary(k => k.Key, k => (IReadOnlySet<By>)k.Value);
    }

    /// <summary>Every writer of <c>iState</c> in the project, with what it writes.</summary>
    public static IReadOnlyList<Writer> Writers(ProjectWalk walk, BehaviorRoot root)
    {
        ArgumentNullException.ThrowIfNull(walk);

        var writers = new List<Writer>();
        void Add(int value, By by, IHavokObject? node = null) => writers.Add(new Writer(value, by, node));

        string rootFile = Path.GetFileName(root.BehaviorFile);
        foreach (ProjectStep step in walk.Steps)
        {
            if (step.Node is hkbBehaviorGraph graph
                && string.Equals(Path.GetFileName(step.File), rootFile, StringComparison.OrdinalIgnoreCase)
                && graph.m_data?.m_stringData?.m_variableNames is { } names
                && graph.m_data.m_variableInitialValues?.m_wordVariableValues is { } values)
            {
                for (int i = 0; i < names.Count && i < values.Count; i++)
                    if (names[i] == "iState") Add(values[i].m_value, By.Initial);
            }

            if (step.Node is BSiStateTaggingGenerator tag) Add(tag.m_iStateToSetAs, By.Tag, tag);

            if (step.Node is BSIStateManagerModifier manager)
                foreach (BSIStateManagerModifierBSiStateData row in manager.m_stateData ?? [])
                    Add(row.m_iStateToSetAs, By.Manager, StateOf(row));
        }

        IReadOnlyDictionary<string, int> constants = StateConstants.Of(walk, root);
        List<string>? expressions = null;

        foreach ((StateAssignment assignment, IHavokObject node) in StateExpressions.WithNodes(walk))
        {
            if (assignment.Base is { } name && constants.TryGetValue(name, out int value))
            {
                if (assignment.Offset is null) Add(value, By.Expression, node);
                else
                    foreach (int offset in RangeOf(assignment.Offset, expressions ??= [.. ExpressionsIn(walk)]))
                        Add(value + offset, By.Expression, node);
            }

            foreach (string choice in assignment.Choices)
                if (constants.TryGetValue(choice, out int chosen)) Add(chosen, By.Expression, node);
                else if (int.TryParse(choice, out int literal)) Add(literal, By.Expression, node);
        }

        return writers;
    }

    // The state a manager row names, so that the row can be placed like a tag.
    private static IHavokObject? StateOf(BSIStateManagerModifierBSiStateData row) =>
        row.m_pStateMachine is hkbStateMachine machine
            ? machine.m_states?.FirstOrDefault(s => s.m_stateId == row.m_StateID)
            : row.m_pStateMachine;

    private static IEnumerable<string> ExpressionsIn(ProjectWalk walk) =>
        walk.Steps.Select(s => s.Node).OfType<hkbEvaluateExpressionModifier>()
            .SelectMany(m => m.m_expressions?.m_expressionsData?.Select(e => e.m_expression) ?? []);

    // The integers a variable is ever assigned, read off the literals on the right of
    // its own assignments; 0 and 1 when nothing assigns it.
    private static IEnumerable<int> RangeOf(string variable, List<string> expressions)
    {
        var values = new SortedSet<int>();
        foreach (string expression in expressions)
        {
            int equals = expression.IndexOf('=');
            if (equals <= 0 || expression[..equals].Trim() != variable) continue;
            foreach (Match match in Literal.Matches(expression[(equals + 1)..]))
                if (int.TryParse(match.Value, out int literal) && Math.Abs(literal) < 64) values.Add(literal);
        }

        return values.Count == 0 ? [0, 1] : values;
    }
}
