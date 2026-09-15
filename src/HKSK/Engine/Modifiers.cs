using HKX2;

namespace HKSK.Engine;

/// <summary>Running the modifiers that change what the graph then selects.</summary>
/// <remarks>
/// <para>
/// Most modifiers move bones, and bones never decide which generator runs. The
/// ones that matter here write variables, because variables are what state
/// machines and blends read: <c>hkbEvaluateExpressionModifier</c> computes them,
/// <c>BSIStateManagerModifier</c> and <c>BSiStateTaggingGenerator</c> write
/// <c>iState</c>. Everything else is recognised and passed over.
/// </para>
/// <para>
/// <c>hkbExpressionData.m_assignmentVariableIndex</c> is <strong>-1 on all 691
/// expressions the game ships</strong>: the runtime resolves the name at
/// activation and the file carries nothing, so the evaluator resolves names
/// itself.
/// </para>
/// </remarks>
public static class Modifiers
{
    private static readonly Dictionary<string, Expression?> Parsed = new(StringComparer.Ordinal);

    /// <summary>Runs a modifier, and returns whether it wrote a variable.</summary>
    /// <remarks>
    /// <strong><c>enable</c> is usually bound.</strong> Ten quadrupeds share one
    /// behaviour file whose root modifier list holds a list per creature -- Bear,
    /// Canine, Cow, Deer, Goat, Horker, Mammoth, SabreCat, Skeever -- every one of
    /// them stored as enabled. What picks the creature is a variable bound to
    /// <c>enable</c>, so reading the field instead of the binding runs all nine and
    /// the last write wins: the deer came out with the skeever's <c>iState</c>.
    /// </remarks>
    public static bool Apply(hkbModifier? modifier, Variables variables, Properties? properties = null)
    {
        if (modifier is null || !Enabled(modifier, variables, properties)) return false;

        switch (modifier)
        {
            case hkbModifierList list:
            {
                bool wrote = false;
                foreach (hkbModifier inner in list.m_modifiers ?? [])
                    wrote |= Apply(inner, variables, properties);

                return wrote;
            }

            case hkbEvaluateExpressionModifier evaluate:
            {
                bool wrote = false;
                foreach (hkbExpressionData data in evaluate.m_expressions?.m_expressionsData ?? [])
                {
                    if (data.m_expression is not { Length: > 0 } text) continue;

                    Expression? expression = Compile(text);
                    if (expression?.Effect.Target is null) continue;

                    wrote |= expression.TryEvaluate(variables, out _);
                }

                return wrote;
            }

            default:
                return false;
        }
    }

    /// <summary>
    /// What a state manager would write, as (variable index, value) for the state
    /// each machine is in.
    /// </summary>
    /// <remarks>
    /// A <c>BSIStateManagerModifier</c> holds a table of (machine, state id) pairs
    /// and the <c>iState</c> each stands for, and writes into the variable its
    /// <c>m_iStateVar</c> names. It cannot be run in isolation: which entry applies
    /// depends on which state every named machine is in, which is what the walk is
    /// working out.
    /// </remarks>
    public static IEnumerable<(int Variable, int Value)> StateWrites(
        hkbModifier? modifier, Func<IHavokObject, int?> stateOf)
    {
        switch (modifier)
        {
            case hkbModifierList list:
                foreach (hkbModifier inner in list.m_modifiers ?? [])
                    foreach ((int variable, int value) in StateWrites(inner, stateOf))
                        yield return (variable, value);

                break;

            case BSIStateManagerModifier manager when manager.m_enable:
                foreach (BSIStateManagerModifierBSiStateData data in manager.m_stateData ?? [])
                    if (data.m_pStateMachine is { } machine && stateOf(machine) == data.m_StateID)
                        yield return (manager.m_iStateVar, data.m_iStateToSetAs);

                break;
        }
    }

    /// <summary>Whether a modifier runs, taking <c>enable</c> from its binding first.</summary>
    public static bool Enabled(hkbModifier modifier, Variables variables, Properties? properties = null) =>
        Bindings.RealOf(modifier, "enable", modifier.m_enable ? 1f : 0f, variables, properties) != 0f;

    /// <summary>Parses once and remembers, since a graph repeats its expressions.</summary>
    private static Expression? Compile(string text)
    {
        lock (Parsed)
        {
            if (Parsed.TryGetValue(text, out Expression? cached)) return cached;

            return Parsed[text] = Expression.Parse(text);
        }
    }
}
