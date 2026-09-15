namespace HKSK.Engine;

/// <summary>The events raised on a character, by name.</summary>
/// <remarks>
/// <para>
/// Event ids are per behaviour file exactly as variable indices are -- each file
/// carries its own <c>hkbBehaviorGraphStringData.m_eventNames</c> -- so an id only
/// means something against the file it was read from, and what is shared between
/// files is the name.
/// </para>
/// <para>
/// Nothing here models a queue or a frame. A character is put in a situation by
/// saying which events are raised, and the graph is then read where it settles;
/// that is the steady state the speed tables describe, not the transition into it.
/// </para>
/// </remarks>
public sealed class Events
{
    private readonly HashSet<string> _raised = new(StringComparer.Ordinal);

    /// <summary>Nothing raised.</summary>
    public static Events None { get; } = new();

    /// <summary>The events named, raised.</summary>
    public static Events Of(params string[] names)
    {
        Events events = new();
        foreach (string name in names) events.Raise(name);

        return events;
    }

    /// <summary>Raises one.</summary>
    public void Raise(string name) => _raised.Add(name);

    /// <summary>Lowers one.</summary>
    public void Lower(string name) => _raised.Remove(name);

    /// <summary>Whether it is raised.</summary>
    public bool Raised(string? name) => name is not null && _raised.Contains(name);

    /// <summary>Every raised name.</summary>
    public IReadOnlyCollection<string> Names => _raised;
}
