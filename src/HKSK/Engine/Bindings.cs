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
    public static int VariableFor(hkbBindable? node, string memberPath)
    {
        IList<hkbVariableBindingSetBinding>? bindings = node?.m_variableBindingSet?.m_bindings;
        if (bindings is null) return -1;

        foreach (hkbVariableBindingSetBinding binding in bindings)
            if ((BindingType)binding.m_bindingType == BindingType.BINDING_TYPE_VARIABLE &&
                string.Equals(binding.m_memberPath, memberPath, StringComparison.Ordinal))
                return binding.m_variableIndex;

        return -1;
    }

    /// <summary>A member's value, taken from the variable bound to it or from the field.</summary>
    public static int IntOf(hkbBindable? node, string memberPath, int stored, Variables variables)
    {
        int at = VariableFor(node, memberPath);
        return at >= 0 && at < variables.Count ? variables.AsInt(at) : stored;
    }

    /// <inheritdoc cref="IntOf"/>
    public static float RealOf(hkbBindable? node, string memberPath, float stored, Variables variables)
    {
        int at = VariableFor(node, memberPath);
        return at >= 0 && at < variables.Count ? variables.AsReal(at) : stored;
    }
}
