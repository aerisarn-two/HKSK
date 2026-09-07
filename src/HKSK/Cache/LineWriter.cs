using System.Text;

namespace HKSK.Cache;

/// <summary>
/// Accumulates cache-file lines, so a block can be measured before it is placed.
/// </summary>
/// <remarks>
/// The merged files prefix every project block with its line count, and that
/// count has to be right or the reader loses its place. Rather than compute it
/// from a formula that has to be kept in step with the writer -- which is how
/// the count and the content drift apart -- a block is written into one of these
/// and then measured.
/// </remarks>
public sealed class LineWriter
{
    private readonly List<string> _lines = [];

    /// <summary>The lines written so far.</summary>
    public IReadOnlyList<string> Lines => _lines;

    /// <summary>How many lines have been written.</summary>
    public int Count => _lines.Count;

    public void Line(string text) => _lines.Add(text);
    public void Blank() => _lines.Add("");
    public void Int(int value) => _lines.Add(CacheText.Int(value));
    public void Float(float value) => _lines.Add(CacheText.Float(value));
    public void Bool(bool value) => _lines.Add(value ? "1" : "0");

    /// <summary>Writes a count followed by the items it counts.</summary>
    public void Counted<T>(IReadOnlyList<T> items, Action<LineWriter, T> write)
    {
        Int(items.Count);
        foreach (T item in items) write(this, item);
    }

    /// <summary>Appends another writer's lines.</summary>
    public void Append(LineWriter other) => _lines.AddRange(other._lines);

    /// <summary>
    /// Renders to text, CRLF-terminated including the final line.
    /// </summary>
    public override string ToString()
    {
        var sb = new StringBuilder();
        foreach (string line in _lines) sb.Append(line).Append(CacheText.NewLine);
        return sb.ToString();
    }
}
