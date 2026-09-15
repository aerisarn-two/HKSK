using HKX2;

namespace HKSK.Behavior;

/// <summary>
/// The variable tables of a project's behaviour files, indexed by file.
/// </summary>
/// <remarks>
/// <para>
/// <strong>A variable index is per file.</strong> Every behaviour packfile carries
/// its own <c>hkbBehaviorGraphData</c> with its own list of names, so index 3 is
/// <c>Speed</c> in one file and something unrelated in the next. A binding is only
/// readable together with the file its node lives in.
/// </para>
/// <para>
/// That is not a pedantic distinction. The dog's speed sampler lives in
/// <c>quadrupedbehavior.hkx</c> while the blends reading its output live in
/// <c>forwardlocomotion.hkx</c>, so anything resolving both against the project's
/// root table gets two wrong answers and no error -- the indices are valid in the
/// wrong table, they just mean something else.
/// </para>
/// <para>
/// Built from a <see cref="ProjectVisitor"/> visit, so the tables are those of the
/// files actually reached rather than of whatever the animation cache lists.
/// </para>
/// </remarks>
public sealed class ProjectVariables
{
    private readonly Dictionary<string, IList<string>> _tables;

    /// <summary>Indexes a visit. The visit is enumerated once.</summary>
    public ProjectVariables(IEnumerable<ProjectStep> walk)
    {
        _tables = new Dictionary<string, IList<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (ProjectStep step in walk)
            if (step.Node is hkbBehaviorGraph graph &&
                graph.m_data?.m_stringData?.m_variableNames is { } names)
                _tables.TryAdd(step.File, names);
    }

    /// <summary>The files a table was found for.</summary>
    public IEnumerable<string> Files => _tables.Keys;

    /// <summary>A file's variable names, in index order. Empty when it has none.</summary>
    public IReadOnlyList<string> Of(string file) =>
        _tables.TryGetValue(file, out IList<string>? names) ? [.. names] : [];

    /// <summary>The name of a variable in a file, or null when the index is out of range.</summary>
    public string? NameOf(string file, int index) =>
        _tables.TryGetValue(file, out IList<string>? names) &&
        index >= 0 && index < names.Count
            ? names[index]
            : null;

    /// <summary>
    /// The name a binding refers to, resolved against the file holding the node it
    /// was read from.
    /// </summary>
    public string? NameOf(ProjectStep step, hkbVariableBindingSetBinding binding) =>
        NameOf(step.File, binding.m_variableIndex);
}
