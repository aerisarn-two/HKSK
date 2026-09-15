using HKSK.Behavior;
using HKSK.Havok;
using HKX2;

namespace HKSK.Engine;

/// <summary>A character's properties: the values that make a shared graph its own.</summary>
/// <remarks>
/// <para>
/// This is how one behaviour file serves ten creatures. Every quadruped loads
/// <c>quadrupedbehavior.hkx</c>, whose root modifier list holds a list per creature
/// with all of them stored as enabled; what picks one is a binding on
/// <c>enable</c> of type <c>BINDING_TYPE_CHARACTER_PROPERTY</c>. The values come
/// from the <em>character</em> file's <c>hkbCharacterData</c>, so the deer and the
/// skeever read the same graph and get different answers.
/// </para>
/// <para>
/// As with variables, the index in a binding is into the behaviour file's own
/// <c>characterPropertyNames</c>, while the value is the character's -- so the
/// index is resolved to a name first and the name looked up once.
/// </para>
/// </remarks>
public sealed class Properties
{
    private readonly Dictionary<string, int> _words = new(StringComparer.Ordinal);
    private readonly Dictionary<string, VariableType> _types = new(StringComparer.Ordinal);

    /// <summary>Nothing declared.</summary>
    public static Properties Empty { get; } = new();

    /// <summary>
    /// The properties a project's character carries, read from the character file.
    /// </summary>
    /// <remarks>
    /// They are not in the behaviour graph and not reached by visiting it: the
    /// project names one character file, and that file's <c>hkbCharacterData</c> is
    /// where the values live.
    /// </remarks>
    public static Properties OfProject(string projectHkx)
    {
        if (BehaviorRoot.Of(projectHkx) is not { } root) return Empty;

        try
        {
            return Of(HavokFile.Load(root.CharacterFile).First<hkbCharacterData>());
        }
        catch (IOException)
        {
            return Empty;
        }
    }

    /// <summary>The properties a character file carries.</summary>
    public static Properties Of(hkbCharacterData? data)
    {
        Properties properties = new();
        if (data is null) return properties;

        IList<string> names = data.m_stringData?.m_characterPropertyNames ?? [];
        IList<hkbVariableValue> values = data.m_characterPropertyValues?.m_wordVariableValues ?? [];
        IList<hkbVariableInfo> infos = data.m_characterPropertyInfos ?? [];

        for (int i = 0; i < names.Count; i++)
        {
            properties._words[names[i]] = i < values.Count ? values[i].m_value : 0;
            properties._types[names[i]] = i < infos.Count
                ? (VariableType)infos[i].m_type
                : VariableType.VARIABLE_TYPE_INT32;
        }

        return properties;
    }

    /// <summary>Whether the character declares it.</summary>
    public bool Has(string name) => _words.ContainsKey(name);

    /// <summary>The value as a real, whatever it is declared as.</summary>
    public float AsReal(string name)
    {
        if (!_words.TryGetValue(name, out int word)) return 0f;

        return _types[name] == VariableType.VARIABLE_TYPE_REAL
            ? BitConverter.Int32BitsToSingle(word)
            : word;
    }

    /// <summary>The value as an integer.</summary>
    public int AsInt(string name) => (int)AsReal(name);

    /// <summary>Every name the character declares.</summary>
    public IEnumerable<string> Names => _words.Keys;
}
