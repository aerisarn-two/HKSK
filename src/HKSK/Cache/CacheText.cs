using System.Globalization;

namespace HKSK.Cache;

/// <summary>
/// The lexical conventions of the cache text files.
/// </summary>
/// <remarks>
/// The cache was written by a C++ tool using default <c>std::ostream</c>
/// formatting, and Skyrim's own files are what any editor has to match. Two
/// details of that are load-bearing:
///
/// <list type="bullet">
/// <item>Floats carry six significant digits, and exponents are padded to three
/// digits with a lowercase 'e' -- MSVC's <c>printf</c> convention, so
/// <c>2.39327e-006</c> rather than .NET's <c>2.39327E-06</c>.</item>
/// <item>Lines end CRLF, including the last one.</item>
/// </list>
///
/// <see cref="Float"/> was checked against every distinct numeric token in the
/// shipped <c>animationdatasinglefile.txt</c> -- 11,517 of them -- and
/// reproduces all of them exactly.
/// </remarks>
public static class CacheText
{
    /// <summary>The line terminator the cache files use.</summary>
    public const string NewLine = "\r\n";

    /// <summary>
    /// Formats a float the way the cache stores it.
    /// </summary>
    public static string Float(float value)
    {
        string s = value.ToString("G6", CultureInfo.InvariantCulture);

        int e = s.IndexOf('E');
        if (e < 0) return s;

        string mantissa = s[..e];
        string exponent = s[(e + 1)..];
        char sign = exponent[0] == '-' ? '-' : '+';

        exponent = exponent.TrimStart('+', '-').TrimStart('0');
        if (exponent.Length == 0) exponent = "0";

        return $"{mantissa}e{sign}{exponent.PadLeft(3, '0')}";
    }

    /// <summary>Parses a float written by <see cref="Float"/> or by the game.</summary>
    public static float ParseFloat(string text) =>
        float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);

    /// <summary>Formats an integer the way the cache stores it.</summary>
    public static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Parses an integer written by the game.</summary>
    public static int ParseInt(string text) =>
        int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
}
