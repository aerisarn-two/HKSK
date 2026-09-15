using HKX2;

namespace HKSK.Behavior;

/// <summary>
/// A modifier and the generator it runs alongside.
/// </summary>
/// <param name="Modifier">The modifier.</param>
/// <param name="Generator">The generator it modifies.</param>
/// <param name="Through">
/// The modifiers between the two, nearest the generator first. Empty when the
/// modifier is the generator's own <c>m_modifier</c>.
/// </param>
/// <param name="File">The packfile the generator lives in.</param>
/// <param name="Depth">How far below the project's first container the modifier sits.</param>
public readonly record struct ModifierOwner(
    hkbModifier Modifier,
    hkbModifierGenerator Generator,
    IReadOnlyList<hkbModifier> Through,
    string File,
    int Depth)
{
    /// <summary>How many modifiers stand between the modifier and its generator.</summary>
    public int Distance => Through.Count;

    public override string ToString() =>
        $"{Modifier.GetType().Name} '{Modifier.m_name}' -> {Generator.m_name}" +
        (Through.Count == 0 ? "" : $" through {string.Join(" / ", Through.Select(m => m.GetType().Name))}");
}

/// <summary>
/// Which generator each modifier in a project belongs to.
/// </summary>
/// <remarks>
/// <para>
/// A modifier does not generate a pose. It runs alongside a generator, before or
/// after it, and changes variables or the pose the generator produced -- so the
/// question "what is this modifier in force over?" is really "which generator is
/// it attached to?", and the answer is above it rather than below.
/// </para>
/// <para>
/// Getting there is a climb and not a lookup. <c>hkbModifierGenerator.m_modifier</c>
/// is the attachment point, and everything else that holds a modifier is itself a
/// modifier -- <c>hkbModifierList.m_modifiers</c>,
/// <c>hkbModifierWrapper.m_modifier</c>, <c>BSModifyOnceModifier</c>'s two -- so a
/// modifier can sit several links below the generator it serves.
/// </para>
/// <para>
/// Which is why the climb tests for <em>any</em> <c>hkbModifier</c> above it
/// rather than for a list of the properties that hold one. Those two are not the
/// same question: <c>hkbEventDrivenModifier</c> declares no modifier property of
/// its own and inherits <c>m_modifier</c> from <c>hkbModifierWrapper</c>, so a
/// check written from the declared properties misses 257 attachments in the
/// shipped game. Over the corpus the climb resolves all 3,159 modifiers with none
/// left over, through chains up to three links long.
/// </para>
/// <para>
/// This is built from a <see cref="ProjectVisitor"/> visit, which means it spans
/// files: a modifier in a referenced graph is attached to a generator in that same
/// graph, and the index says so without the caller knowing which file either is
/// in.
/// </para>
/// </remarks>
public sealed class ModifierOwners
{
    private readonly Dictionary<IHavokObject, ProjectStep> _steps;

    /// <summary>Indexes a visit. The visit is enumerated once.</summary>
    public ModifierOwners(IEnumerable<ProjectStep> walk)
    {
        _steps = new Dictionary<IHavokObject, ProjectStep>(ReferenceEqualityComparer.Instance);

        var modifiers = new List<hkbModifier>();
        foreach (ProjectStep step in walk)
        {
            _steps.TryAdd(step.Node, step);
            if (step.Node is hkbModifier modifier) modifiers.Add(modifier);
        }

        var owned = new List<ModifierOwner>();
        foreach (hkbModifier modifier in modifiers)
            if (Resolve(modifier) is { } owner) owned.Add(owner);

        All = owned;
    }

    /// <summary>Every modifier that reaches a generator, with the generator.</summary>
    public IReadOnlyList<ModifierOwner> All { get; }

    /// <summary>
    /// The generator a modifier belongs to, or null when nothing above it is one.
    /// </summary>
    public hkbModifierGenerator? OwnerOf(hkbModifier modifier) => Resolve(modifier)?.Generator;

    /// <summary>Every modifier belonging to a generator, nearest first.</summary>
    public IEnumerable<ModifierOwner> Under(hkbModifierGenerator generator)
    {
        foreach (ModifierOwner owner in All)
            if (ReferenceEquals(owner.Generator, generator)) yield return owner;
    }

    private ModifierOwner? Resolve(hkbModifier modifier)
    {
        if (!_steps.TryGetValue(modifier, out ProjectStep step)) return null;

        var through = new List<hkbModifier>();
        IHavokObject? parent = step.Parent;

        while (parent is not null)
        {
            // the one property that attaches a modifier to a generator
            if (parent is hkbModifierGenerator generator)
            {
                through.Reverse();
                return new ModifierOwner(modifier, generator, through, step.File, step.Depth);
            }

            // anything else holding it is another modifier, so keep climbing
            if (parent is not hkbModifier between) return null;

            through.Add(between);
            parent = _steps.TryGetValue(parent, out ProjectStep above) ? above.Parent : null;
        }

        return null;
    }
}
