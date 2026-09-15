using HKX2;

namespace HKSK.Engine;

/// <summary>A behaviour graph's word variables, as the runtime holds them.</summary>
/// <remarks>
/// <para>
/// Havok keeps every word variable in one array of 32-bit slots and decides how to
/// read each slot from <c>hkbVariableInfo.m_type</c>. The type is not decoration:
/// the runtime switches on it when it copies a variable into the member bound to
/// it, so a variable declared <c>BOOL</c> never writes a real member, and every
/// binding on the graph silently does nothing. That was measured against the 6.6
/// runtime -- see <c>reverse-hbt-6.6.md</c> §4.
/// </para>
/// <para>
/// The values a graph starts from are its <c>variableInitialValues</c>, which is
/// what the file carries. Nothing here models the game writing them.
/// </para>
/// </remarks>
public sealed class Variables
{
    private readonly int[] _words;
    private readonly VariableType[] _types;
    private readonly string[] _names;
    private readonly Dictionary<string, int> _byName;

    private Variables(int[] words, VariableType[] types, string[] names)
    {
        _words = words;
        _types = types;
        _names = names;

        _byName = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < names.Length; i++) _byName.TryAdd(names[i], i);
    }

    /// <summary>A table with nothing in it.</summary>
    public static Variables Empty { get; } = new([], [], []);

    /// <summary>The table a graph starts with, or an empty one when it declares none.</summary>
    public static Variables Of(hkbBehaviorGraph graph)
    {
        hkbBehaviorGraphData? data = graph.m_data;
        IList<hkbVariableInfo> infos = data?.m_variableInfos ?? [];
        IList<string> names = data?.m_stringData?.m_variableNames ?? [];
        IList<hkbVariableValue> values = data?.m_variableInitialValues?.m_wordVariableValues ?? [];

        int count = Math.Max(infos.Count, names.Count);
        int[] words = new int[count];
        VariableType[] types = new VariableType[count];
        string[] named = new string[count];

        for (int i = 0; i < count; i++)
        {
            words[i] = i < values.Count ? values[i].m_value : 0;
            types[i] = i < infos.Count ? (VariableType)infos[i].m_type : VariableType.VARIABLE_TYPE_INT32;
            named[i] = i < names.Count ? names[i] : $"#{i}";
        }

        return new Variables(words, types, named);
    }

    /// <summary>How many word variables the graph declares.</summary>
    public int Count => _words.Length;

    /// <summary>The index of a name, or -1.</summary>
    public int IndexOf(string name) => _byName.TryGetValue(name, out int at) ? at : -1;

    /// <summary>The name of an index.</summary>
    public string NameOf(int index) => _names[index];

    /// <summary>How the slot is read.</summary>
    public VariableType TypeOf(int index) => _types[index];

    /// <summary>The raw slot, whatever its type.</summary>
    public int Word(int index) => _words[index];

    /// <summary>The slot as a real.</summary>
    public float Real(int index) => BitConverter.Int32BitsToSingle(_words[index]);

    /// <summary>The slot as an integer.</summary>
    public int Int(int index) => _words[index];

    /// <summary>The slot as a bool.</summary>
    public bool Bool(int index) => _words[index] != 0;

    /// <summary>
    /// The slot as a real whatever it is declared as, which is what a member of
    /// real type sees when the variable is bound to it.
    /// </summary>
    public float AsReal(int index) => _types[index] switch
    {
        VariableType.VARIABLE_TYPE_REAL => Real(index),
        _ => _words[index],
    };

    /// <summary>
    /// The slot as an integer whatever it is declared as. A real-typed variable
    /// bound to an integer member is truncated, not reinterpreted.
    /// </summary>
    public int AsInt(int index) => _types[index] switch
    {
        VariableType.VARIABLE_TYPE_REAL => (int)Real(index),
        _ => _words[index],
    };

    /// <summary>Writes a real into a slot, in the representation its type calls for.</summary>
    public void Set(int index, float value) =>
        _words[index] = _types[index] == VariableType.VARIABLE_TYPE_REAL
            ? BitConverter.SingleToInt32Bits(value)
            : (int)value;

    /// <summary>Writes an integer into a slot.</summary>
    public void Set(int index, int value) =>
        _words[index] = _types[index] == VariableType.VARIABLE_TYPE_REAL
            ? BitConverter.SingleToInt32Bits(value)
            : value;

    /// <summary>Writes by name. False when no such variable is declared.</summary>
    public bool Set(string name, float value)
    {
        int at = IndexOf(name);
        if (at < 0) return false;

        Set(at, value);
        return true;
    }

    /// <summary>Every name, in index order.</summary>
    public IReadOnlyList<string> Names => _names;
}
