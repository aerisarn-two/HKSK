using HKX2;

namespace HKSK.Engine;

/// <summary>One character's variables, shared by name across its behaviour files.</summary>
/// <remarks>
/// <para>
/// Indices are per file and values are not. Ten creatures share
/// <c>quadrupedbehavior.hkx</c>, and the expression that decides their movement
/// state -- <c>iState = iState_DeerDefault + iMovementSpeed</c> -- lives in that
/// shared file. If each file kept its own values, every quadruped would come out
/// with the same <c>iState</c>: the shared file declares
/// <c>iState_DeerDefault</c> as 10 while the deer's own file declares it as 20,
/// and it is the deer's that the speed tables are numbered by.
/// </para>
/// <para>
/// So a name is one variable per character, and the file that declares it first --
/// the root, which the visit reaches first -- gives it its initial value.
/// </para>
/// </remarks>
public sealed class VariableSpace
{
    private readonly Dictionary<string, int> _words = new(StringComparer.Ordinal);
    private readonly Dictionary<string, VariableType> _types = new(StringComparer.Ordinal);

    /// <summary>Declares a name, keeping the first declaration's value and type.</summary>
    public void Declare(string name, int word, VariableType type)
    {
        if (_words.ContainsKey(name)) return;

        _words[name] = word;
        _types[name] = type;
    }

    /// <summary>Whether the character has such a variable at all.</summary>
    public bool Has(string name) => _words.ContainsKey(name);

    /// <summary>The raw slot.</summary>
    public int Word(string name) => _words.TryGetValue(name, out int word) ? word : 0;

    /// <summary>How the slot is read.</summary>
    public VariableType TypeOf(string name) =>
        _types.TryGetValue(name, out VariableType type) ? type : VariableType.VARIABLE_TYPE_INT32;

    /// <summary>Writes a slot, in the representation its declared type calls for.</summary>
    public void Set(string name, int word) => _words[name] = word;

    /// <summary>Every name the character declares.</summary>
    public IEnumerable<string> Names => _words.Keys;
}
