using System.Numerics;
using System.Text;

namespace HKSK.SetData;

/// <summary>A set of a project's animation files, by their index in <see cref="ProjectFiles"/>.</summary>
/// <remarks>
/// The generator holds one of these for every event under every weapon combination the
/// game asks about -- on the player, hundreds of events over 121 combinations -- so it is
/// a bitset rather than a set of paths.
/// </remarks>
internal sealed class FileSet
{
    private ulong[] _bits;

    public FileSet(int capacity = 0) => _bits = new ulong[(capacity + 63) / 64];

    private FileSet(ulong[] bits) => _bits = bits;

    public FileSet Clone() => new((ulong[])_bits.Clone());

    public void Add(int index)
    {
        Grow(index / 64 + 1);
        _bits[index / 64] |= 1UL << (index % 64);
    }

    public bool Contains(int index) => index / 64 < _bits.Length && (_bits[index / 64] & (1UL << (index % 64))) != 0;

    public int Count => _bits.Sum(BitOperations.PopCount);

    public void UnionWith(FileSet other)
    {
        Grow(other._bits.Length);
        for (int i = 0; i < other._bits.Length; i++) _bits[i] |= other._bits[i];
    }

    public void ExceptWith(FileSet other)
    {
        for (int i = 0; i < Math.Min(_bits.Length, other._bits.Length); i++) _bits[i] &= ~other._bits[i];
    }

    public void IntersectWith(FileSet other)
    {
        for (int i = 0; i < _bits.Length; i++) _bits[i] &= i < other._bits.Length ? other._bits[i] : 0;
    }

    /// <summary>How many files a union with another would hold.</summary>
    public int UnionCount(FileSet other)
    {
        int count = 0;
        for (int i = 0; i < Math.Max(_bits.Length, other._bits.Length); i++)
            count += BitOperations.PopCount((i < _bits.Length ? _bits[i] : 0) | (i < other._bits.Length ? other._bits[i] : 0));
        return count;
    }

    public IEnumerable<int> Indices()
    {
        for (int i = 0; i < _bits.Length; i++)
            for (ulong word = _bits[i]; word != 0; word &= word - 1)
                yield return i * 64 + BitOperations.TrailingZeroCount(word);
    }

    /// <summary>A value two sets share exactly when they hold the same files.</summary>
    public string Key()
    {
        int used = _bits.Length;
        while (used > 0 && _bits[used - 1] == 0) used--;

        var text = new StringBuilder(used * 16);
        for (int i = 0; i < used; i++) text.Append(_bits[i].ToString("x16"));
        return text.ToString();
    }

    private void Grow(int words)
    {
        if (words > _bits.Length) Array.Resize(ref _bits, words);
    }
}
