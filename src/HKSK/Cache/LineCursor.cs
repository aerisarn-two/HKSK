namespace HKSK.Cache;

/// <summary>
/// Reads a cache file as a sequence of lines, reporting where it gave up.
/// </summary>
/// <remarks>
/// The cache format is a counted-block grammar with no delimiters and no
/// self-description: every list is a count followed by that many lines, and a
/// single miscount silently reinterprets the rest of the file as something else.
/// Failing loudly at the offending line number is the only practical way to
/// debug one, so every read goes through here.
/// </remarks>
public sealed class LineCursor
{
    private readonly IReadOnlyList<string> _lines;
    private readonly int _end;
    private int _at;

    public LineCursor(IReadOnlyList<string> lines, int start = 0, int end = -1)
    {
        _lines = lines;
        _at = start;
        _end = end < 0 ? lines.Count : end;
    }

    /// <summary>The 0-based index of the next line to be read.</summary>
    public int Position => _at;

    /// <summary>Whether any line remains.</summary>
    public bool More => _at < _end;

    /// <summary>Reads the next line.</summary>
    public string Line(string what)
    {
        if (_at >= _end) throw Fail(what, "end of block");
        return _lines[_at++];
    }

    /// <summary>Reads the next line as an integer.</summary>
    public int Int(string what)
    {
        string line = Line(what);
        return int.TryParse(line, out int value) ? value : throw Fail(what, line);
    }

    /// <summary>Reads the next line as a float.</summary>
    public float Float(string what)
    {
        string line = Line(what);
        try { return CacheText.ParseFloat(line); }
        catch (FormatException) { throw Fail(what, line); }
    }

    /// <summary>
    /// Reads a count and returns a cursor over exactly that many following
    /// lines, having stepped this cursor past them.
    /// </summary>
    public LineCursor Counted(string what)
    {
        int start = _at;
        int count = Int(what);
        if (count < 0) throw Fail(what, count.ToString());

        int from = _at;
        int to = from + count;
        if (to > _end)
            throw new InvalidDataException(
                $"line {start + 1}: {what} claims {count} lines but only {_end - from} remain");

        _at = to;
        return new LineCursor(_lines, from, to);
    }

    /// <summary>Steps over a blank separator line if one is next.</summary>
    public void SkipBlank()
    {
        if (_at < _end && _lines[_at].Length == 0) _at++;
    }

    /// <summary>Reports a parse failure against the current line.</summary>
    public InvalidDataException Fail(string what, string saw) =>
        new($"line {_at}: expected {what}, saw '{saw}'");
}
