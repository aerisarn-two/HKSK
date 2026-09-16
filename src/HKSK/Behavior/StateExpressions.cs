using System.Text.RegularExpressions;
using HKX2;

namespace HKSK.Behavior;

/// <summary>
/// An expression in the graph that assigns <c>iState</c>.
/// </summary>
/// <param name="Text">The expression as written.</param>
/// <param name="Base">
/// The <c>iState_&lt;MOVT&gt;</c> constant the value is built from, when there is a
/// single one.
/// </param>
/// <param name="Offset">
/// The variable added to <paramref name="Base"/>, for the
/// <c>iState = iState_X + n</c> form. Null for the other forms.
/// </param>
/// <param name="Choices">
/// Every constant the expression can produce, which is more than one for a
/// <c>cond(...)</c>.
/// </param>
/// <param name="File">The packfile the expression lives in.</param>
public readonly record struct StateAssignment(
    string Text,
    string? Base,
    string? Offset,
    IReadOnlyList<string> Choices,
    string File)
{
    public override string ToString() => Text;
}

/// <summary>
/// The expressions a behaviour uses to set <c>iState</c>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is the third way a graph writes <c>iState</c>, and the one that is
/// easiest to miss.</strong> A <c>BSiStateTaggingGenerator</c> carries the key in a
/// field and a <c>BSIStateManagerModifier</c> carries it in a binding, but a
/// <c>hkbEvaluateExpressionModifier</c> carries it in a <em>string</em>, so a search
/// over bindings and index fields reports that nothing writes <c>iState</c> when 22
/// of the 49 projects do.
/// </para>
/// <para>
/// Three forms occur. A bare constant, <c>iState = iState_SteamDefault</c>. A
/// choice, <c>iState = cond(isSwimming == 1, iState_BearSwimDefault,
/// iState_BearDefault)</c>. And an offset from a base:
/// </para>
/// <code>
/// iMovementSpeed = cond((Speed &lt; 100), 0, 1)
/// iState         = iState_DeerDefault + iMovementSpeed
/// </code>
/// <para>
/// The third is the useful one, because the same variable also drives the state
/// machine's <c>startStateId</c>. So the key and the state are two readings of one
/// number: state <c>#n</c> serves key <c>base + n</c>, stated by the graph rather
/// than guessed. It is also where the decade numbering comes from -- the base is the
/// decade and the offset is the variant.
/// </para>
/// </remarks>
public static class StateExpressions
{
    private static readonly Regex Assignment =
        new(@"^\s*iState\s*=\s*(?<rhs>.+?)\s*$", RegexOptions.Compiled);

    private static readonly Regex Offset =
        new(@"^\s*(?<base>iState_\w+)\s*\+\s*(?<offset>\w+)\s*$", RegexOptions.Compiled);

    private static readonly Regex Constant =
        new(@"iState_\w+", RegexOptions.Compiled);

    /// <summary>
    /// The locomotion state an assignment governs, when it governs exactly one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An expression sits inside the subtree it applies to: the draugr writes
    /// <c>iState = iState_DraugrH2H</c> inside the hand-to-hand branch and
    /// <c>iState = iState_DraugrBow</c> inside the bow branch. So the state it means
    /// is the one below the nearest ancestor they share -- and only when that
    /// ancestor has a single locomotion state under it, since an ancestor high
    /// enough covers them all and says nothing.
    /// </para>
    /// <para>
    /// <strong>Unless it is the one that undoes it.</strong>
    /// <c>BSModifyOnceModifier</c> carries two modifiers, one run on entering its
    /// subtree and one on leaving, and Bethesda uses the pair to set a variable and
    /// put it back. The horker's swim state holds both: <c>HorkerSwimmingStart_EEM</c>
    /// writing <c>iState = iState_HorkerSwimDefault</c> and
    /// <c>HorkerSwimmingStop_EEM</c> writing <c>iState = iState_HorkerDefault</c>,
    /// on the same node, in the same list. The second sits inside the swim branch
    /// and means the opposite of it, so an expression reached through
    /// <c>m_pOnDeactivateModifier</c> governs nothing.
    /// </para>
    /// </remarks>
    public static IHavokObject? GovernedBy(
        ProjectWalk walk, IHavokObject expression, IReadOnlyList<IHavokObject> states)
    {
        ArgumentNullException.ThrowIfNull(walk);
        ArgumentNullException.ThrowIfNull(states);

        IHavokObject below = expression;

        foreach (ProjectStep above in walk.Ancestors(expression))
        {
            if (above.Node is BSModifyOnceModifier once &&
                once.m_pOnDeactivateModifier is { } leaving &&
                ReferenceEquals(leaving, below))
                return null;

            below = above.Node;

            var under = new List<IHavokObject>();

            foreach (IHavokObject state in states)
                if (ReferenceEquals(state, above.Node) ||
                    walk.Ancestors(state).Any(a => ReferenceEquals(a.Node, above.Node)))
                    under.Add(state);

            if (under.Count == 1) return under[0];
            if (under.Count > 1) return null;      // too high to tell them apart
        }

        return null;
    }

    /// <summary>The node an assignment was read from, paired with the assignment.</summary>
    public static IEnumerable<(StateAssignment Assignment, IHavokObject Node)> WithNodes(ProjectWalk walk)
    {
        ArgumentNullException.ThrowIfNull(walk);

        foreach (ProjectStep step in walk.Steps)
        {
            if (step.Node is not hkbEvaluateExpressionModifier modifier) continue;

            foreach (StateAssignment assignment in Parse(modifier, step.File))
                yield return (assignment, step.Node);
        }
    }

    /// <summary>Every <c>iState</c> assignment the project's graphs contain.</summary>
    public static IEnumerable<StateAssignment> In(ProjectWalk walk)
    {
        ArgumentNullException.ThrowIfNull(walk);

        foreach (ProjectStep step in walk.Steps)
            if (step.Node is hkbEvaluateExpressionModifier modifier)
                foreach (StateAssignment assignment in Parse(modifier, step.File))
                    yield return assignment;
    }

    private static IEnumerable<StateAssignment> Parse(hkbEvaluateExpressionModifier modifier, string file)
    {
        foreach (hkbExpressionData data in modifier.m_expressions?.m_expressionsData ?? [])
        {
            Match assignment = Assignment.Match(data.m_expression ?? "");
            if (!assignment.Success) continue;

            string rhs = assignment.Groups["rhs"].Value;
            List<string> choices = [.. Constant.Matches(rhs).Select(m => m.Value).Distinct()];
            if (choices.Count == 0) continue;

            Match offset = Offset.Match(rhs);

            yield return new StateAssignment(
                data.m_expression!.Trim(),
                offset.Success ? offset.Groups["base"].Value : choices.Count == 1 ? choices[0] : null,
                offset.Success ? offset.Groups["offset"].Value : null,
                choices,
                file);
        }
    }
}
