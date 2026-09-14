using System.Collections;
using System.Reflection;
using HKX2;
using Type = System.Type;

namespace HKSK.Behavior;

/// <summary>
/// The child objects a Havok node holds, found by reflection rather than by a
/// hand-written list of node types.
/// </summary>
/// <remarks>
/// <para>
/// A behaviour graph is a tree of a hundred-odd node classes, and each one names
/// its children differently: a blender has <c>m_children</c> of wrapper structs
/// that each hold <c>m_generator</c>, a state machine has <c>m_states</c> of
/// wrappers that hold <c>m_generator</c>, a modifier generator has a bare
/// <c>m_generator</c>, <c>BSiStateTaggingGenerator</c> has
/// <c>m_pDefaultGenerator</c>, <c>BSCyclicBlendTransitionGenerator</c> has
/// <c>m_pBlenderGenerator</c>, and so on.
/// </para>
/// <para>
/// Enumerating those by hand is how a walk silently loses a subtree: it does not
/// fail, it just returns less, and nothing says which node type was forgotten.
/// So the edges are read off the generated classes instead -- every property
/// whose value is a Havok object, or a list of them -- which is exhaustive by
/// construction and stays correct when the type set grows.
/// </para>
/// </remarks>
public static class BehaviorEdges
{
    private static readonly Dictionary<Type, PropertyInfo[]> Cached = [];
    private static readonly object Gate = new();

    /// <summary>Every child of a node, with the property each was reached through.</summary>
    public static IEnumerable<(string Member, int Index, IHavokObject Child)> Of(IHavokObject node)
    {
        foreach (PropertyInfo property in PropertiesOf(node.GetType()))
        {
            object? value = property.GetValue(node);
            if (value is null) continue;

            if (value is IHavokObject one)
            {
                yield return (property.Name, -1, one);
                continue;
            }

            if (value is not IEnumerable many) continue;

            int index = 0;
            foreach (object? item in many)
            {
                if (item is IHavokObject child) yield return (property.Name, index, child);
                index++;
            }
        }
    }

    /// <summary>
    /// The properties of a node type that can hold children, worked out once.
    /// </summary>
    /// <remarks>
    /// <c>IList&lt;object&gt;</c> shows up on several classes for fields the
    /// reader does not model, so a list is admitted on the evidence of what is in
    /// it rather than on its declared element type.
    /// </remarks>
    private static PropertyInfo[] PropertiesOf(Type type)
    {
        lock (Gate)
        {
            if (Cached.TryGetValue(type, out PropertyInfo[]? known)) return known;

            PropertyInfo[] found = [.. type
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetIndexParameters().Length == 0)
                .Where(p => typeof(IHavokObject).IsAssignableFrom(p.PropertyType) ||
                            (typeof(IEnumerable).IsAssignableFrom(p.PropertyType) &&
                             p.PropertyType != typeof(string)))];

            Cached[type] = found;
            return found;
        }
    }
}
