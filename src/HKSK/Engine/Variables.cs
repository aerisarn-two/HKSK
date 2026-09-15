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
    private readonly string[] _names;
    private readonly Dictionary<string, int> _byName;
    private readonly VariableSpace _space;
    private readonly string[] _propertyNames;
    private readonly string[] _eventNames;

    private Variables(int[] words, VariableType[] types, string[] names,
                      VariableSpace? space = null, string[]? propertyNames = null,
                      string[]? eventNames = null)
    {
        _names = names;
        _propertyNames = propertyNames ?? [];
        _eventNames = eventNames ?? [];
        _space = space ?? new VariableSpace();

        _byName = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < names.Length; i++) _byName.TryAdd(names[i], i);

        for (int i = 0; i < names.Length; i++) _space.Declare(names[i], words[i], types[i]);
    }

    /// <summary>The character-wide store these indices name into.</summary>
    public VariableSpace Space => _space;

    /// <summary>A table with nothing in it.</summary>
    public static Variables Empty { get; } = new([], [], []);

    /// <summary>The table a graph starts with, or an empty one when it declares none.</summary>
    public static Variables Of(hkbBehaviorGraph graph) => Of(graph, null);

    /// <summary>The table a graph declares, sharing values by name with its siblings.</summary>
    public static Variables Of(hkbBehaviorGraph graph, VariableSpace? space)
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

        return new Variables(words, types, named, space,
            [.. data?.m_stringData?.m_characterPropertyNames ?? []],
            [.. data?.m_stringData?.m_eventNames ?? []]);
    }

    /// <summary>A table built directly, for a graph assembled rather than read.</summary>
    public static Variables Of(params (string Name, float Value)[] reals)
    {
        int[] words = new int[reals.Length];
        VariableType[] types = new VariableType[reals.Length];
        string[] names = new string[reals.Length];

        for (int i = 0; i < reals.Length; i++)
        {
            words[i] = BitConverter.SingleToInt32Bits(reals[i].Value);
            types[i] = VariableType.VARIABLE_TYPE_REAL;
            names[i] = reals[i].Name;
        }

        return new Variables(words, types, names);
    }

    /// <summary>How many word variables the graph declares.</summary>
    public int Count => _names.Length;

    /// <summary>The index of a name, or -1.</summary>
    public int IndexOf(string name) => _byName.TryGetValue(name, out int at) ? at : -1;

    /// <summary>The name of an index.</summary>
    public string NameOf(int index) => _names[index];

    /// <summary>How the slot is read.</summary>
    public VariableType TypeOf(int index) => _space.TypeOf(_names[index]);

    /// <summary>The raw slot, whatever its type.</summary>
    public int Word(int index) => _space.Word(_names[index]);

    /// <summary>The slot as a real.</summary>
    public float Real(int index) => BitConverter.Int32BitsToSingle(Word(index));

    /// <summary>The slot as an integer.</summary>
    public int Int(int index) => Word(index);

    /// <summary>The slot as a bool.</summary>
    public bool Bool(int index) => Word(index) != 0;

    /// <summary>
    /// The slot as a real whatever it is declared as, which is what a member of
    /// real type sees when the variable is bound to it.
    /// </summary>
    public float AsReal(int index) => TypeOf(index) switch
    {
        VariableType.VARIABLE_TYPE_REAL => Real(index),
        _ => Word(index),
    };

    /// <summary>
    /// The slot as an integer whatever it is declared as. A real-typed variable
    /// bound to an integer member is truncated, not reinterpreted.
    /// </summary>
    public int AsInt(int index) => TypeOf(index) switch
    {
        VariableType.VARIABLE_TYPE_REAL => (int)Real(index),
        _ => Word(index),
    };

    /// <summary>Writes a real into a slot, in the representation its type calls for.</summary>
    public void Set(int index, float value) =>
        _space.Set(_names[index], TypeOf(index) == VariableType.VARIABLE_TYPE_REAL
            ? BitConverter.SingleToInt32Bits(value)
            : (int)value);

    /// <summary>Writes an integer into a slot.</summary>
    public void Set(int index, int value) =>
        _space.Set(_names[index], TypeOf(index) == VariableType.VARIABLE_TYPE_REAL
            ? BitConverter.SingleToInt32Bits(value)
            : value);

    /// <summary>Holds a variable so the graph cannot write it. See <see cref="VariableSpace.Pin"/>.</summary>
    public bool Pin(string name)
    {
        if (IndexOf(name) < 0 && !_space.Has(name)) return false;

        _space.Pin(name);
        return true;
    }

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

    /// <summary>
    /// The character property an index names, in this file. Property indices are
    /// per file exactly as variable indices are.
    /// </summary>
    public string? PropertyNameOf(int index) =>
        index >= 0 && index < _propertyNames.Length ? _propertyNames[index] : null;

    /// <summary>The event an id names, in this file. Event ids are per file too.</summary>
    public string? EventNameOf(int id) =>
        id >= 0 && id < _eventNames.Length ? _eventNames[id] : null;
}
