using HKX2;

namespace HKSK.Engine;

/// <summary>Which variable drives a member, when one does.</summary>
/// <remarks>
/// A binding set hangs off any <c>hkbBindable</c> and names members by path. The
/// runtime resolves the path to an offset once, at activation, and then copies the
/// variable into the member every frame; nothing of that resolution is stored in
/// the file. Here the path is matched by name, which is enough for the members
/// that select -- <c>startStateId</c>, <c>weight</c>,
/// <c>selectedGeneratorIndex</c>, <c>blendParameter</c>.
/// </remarks>
public static class Bindings
{
    /// <summary>The variable index bound to a member, or -1 when none is.</summary>
    public static int VariableFor(hkbBindable? node, string memberPath) =>
        Find(node, memberPath, BindingType.BINDING_TYPE_VARIABLE);

    /// <summary>The character-property index bound to a member, or -1.</summary>
    public static int PropertyFor(hkbBindable? node, string memberPath) =>
        Find(node, memberPath, BindingType.BINDING_TYPE_CHARACTER_PROPERTY);

    private static int Find(hkbBindable? node, string memberPath, BindingType kind)
    {
        IList<hkbVariableBindingSetBinding>? bindings = node?.m_variableBindingSet?.m_bindings;
        if (bindings is null) return -1;

        foreach (hkbVariableBindingSetBinding binding in bindings)
            if ((BindingType)binding.m_bindingType == kind &&
                string.Equals(binding.m_memberPath, memberPath, StringComparison.Ordinal))
                return binding.m_variableIndex;

        return -1;
    }

    /// <summary>
    /// A member's value: from the variable bound to it, else from the character
    /// property bound to it, else the stored field.
    /// </summary>
    public static float RealOf(
        hkbBindable? node, string memberPath, float stored, Variables variables,
        Properties? properties = null)
    {
        int at = VariableFor(node, memberPath);
        if (at >= 0 && at < variables.Count) return variables.AsReal(at);

        if (properties is not null &&
            variables.PropertyNameOf(PropertyFor(node, memberPath)) is { } named &&
            properties.Has(named))
            return properties.AsReal(named);

        return stored;
    }

    /// <inheritdoc cref="RealOf"/>
    public static int IntOf(
        hkbBindable? node, string memberPath, int stored, Variables variables,
        Properties? properties = null) =>
        (int)RealOf(node, memberPath, stored, variables, properties);
}
