using HKX2;

namespace HKSK.Behavior;

/// <summary>
/// The <c>iState_&lt;movement type&gt;</c> constants a project declares.
/// </summary>
/// <remarks>
/// <para>
/// These are the dictionary between the speed table and the movement types. A
/// table block is keyed by a number; a movement type is named; and a variable
/// called <c>iState_Falmer1HMWalk</c> holding the value 2 says that block 2 is the
/// <c>Falmer1HMWalk</c> movement type. They are constants -- nothing in any graph
/// writes them -- and the game writes the matching number into <c>iState</c> from
/// whichever movement type the actor is using.
/// </para>
/// <para>
/// <strong>Read from the root behaviour graph only.</strong> A referenced graph
/// carries its own table, and a shared one carries the union for everything that
/// shares it: <c>quadrupedbehavior.hkx</c> declares 17 or 18 of these, being every
/// quadruped's, while the dog's own root graph declares its 2. Counting across
/// files inflates 145 real constants to 310.
/// </para>
/// </remarks>
public static class StateConstants
{
    /// <summary>
    /// The constants of a project's root graph, by name, with the value each holds.
    /// </summary>
    public static IReadOnlyDictionary<string, int> Of(ProjectWalk walk, BehaviorRoot root)
    {
        var constants = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        string wanted = Path.GetFullPath(root.BehaviorFile);

        foreach (ProjectStep step in walk.Steps)
        {
            if (step.Node is not hkbBehaviorGraph graph) continue;
            if (!string.Equals(Path.GetFullPath(step.File), wanted, StringComparison.OrdinalIgnoreCase)) continue;

            IList<string>? names = graph.m_data?.m_stringData?.m_variableNames;
            IList<hkbVariableValue>? values = graph.m_data?.m_variableInitialValues?.m_wordVariableValues;
            if (names is null || values is null) continue;

            for (int i = 0; i < names.Count && i < values.Count; i++)
                if (names[i].StartsWith("iState_", StringComparison.OrdinalIgnoreCase))
                    constants[names[i]] = values[i].m_value;
        }

        return constants;
    }

    /// <summary>The movement type a key names, or null when the project declares none for it.</summary>
    public static string? MovementTypeOf(IReadOnlyDictionary<string, int> constants, int key)
    {
        foreach ((string name, int value) in constants)
            if (value == key) return name["iState_".Length..];

        return null;
    }
}
