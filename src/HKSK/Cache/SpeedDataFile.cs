using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace HKSK.Cache;

/// <summary>
/// One sampled point: the speed the game asked for, and the speed it gets.
/// </summary>
/// <remarks>
/// Both are game units/s on the same axis, the one RACE <c>MOVT</c> is written
/// in. For a correctly authored creature they are the same number; the file
/// exists because they drift. See <c>docs/speed-data.md</c> §0.
/// </remarks>
public readonly record struct SpeedPoint(float X, float Y)
{
    public override string ToString() =>
        $"({X.ToString("G6", CultureInfo.InvariantCulture)}, {Y.ToString("G6", CultureInfo.InvariantCulture)})";
}

/// <summary>
/// The response curve for one heading: <c>(goal speed -&gt; delivered speed)</c>.
/// </summary>
/// <remarks>
/// <see cref="Direction"/> is a compass in [0,1]: 0.00 ahead, 0.25 right, 0.50
/// behind, 0.75 left. An entry carries 19 of these, 0.00 to 0.90 in steps of
/// 0.05; there is no record past 0.90, so a heading in [0.95, 1.0) resolves
/// against the last one.
///
/// The points are the breakpoints of a piecewise curve, not a dense sampling --
/// a median record keeps about 11 of some 650 swept positions -- and the
/// consumer interpolates linearly between them.
/// </remarks>
public sealed class SpeedRecord
{
    /// <summary>The heading this curve is for.</summary>
    public float Direction { get; set; }

    /// <summary>The retained breakpoints, in non-decreasing <c>X</c> order.</summary>
    public List<SpeedPoint> Points { get; set; } = [];

    /// <summary>
    /// Reads the curve at a requested speed, as the engine does.
    /// </summary>
    /// <remarks>
    /// Clamps below the first point and above the last, and interpolates
    /// linearly between the two that bracket <paramref name="goalSpeed"/>.
    /// Returns <paramref name="goalSpeed"/> unchanged for an empty curve, which
    /// is what the engine does when it has no database at all.
    /// </remarks>
    public float Sample(float goalSpeed)
    {
        if (Points.Count == 0) return goalSpeed;
        if (goalSpeed <= Points[0].X) return Points[0].Y;

        for (int i = 1; i < Points.Count; i++)
        {
            SpeedPoint b = Points[i];
            if (goalSpeed > b.X) continue;

            SpeedPoint a = Points[i - 1];
            float span = b.X - a.X;
            if (span <= 0f) return b.Y;

            return a.Y + (b.Y - a.Y) * ((goalSpeed - a.X) / span);
        }

        return Points[^1].Y;
    }

    /// <summary>The 19 direction values an entry's records carry, in order.</summary>
    /// <remarks>
    /// Float <em>accumulation</em> of <c>+0.05f</c>, which is not the same as
    /// <c>0.05f * i</c>: the two diverge from i = 7, and the shipped files are
    /// bit-identical to accumulation. A writer that multiplies will produce a
    /// file that differs from Bethesda's in 12 of every 19 records.
    /// </remarks>
    public static IEnumerable<float> StandardDirections()
    {
        for (float d = 0.0f; d < 0.95f; d += 0.05f) yield return d;
    }
}

/// <summary>
/// One locomotion state's table: 19 curves, one per heading.
/// </summary>
/// <remarks>
/// <see cref="Key"/> is the value of the behaviour graph's <c>iState</c>
/// variable, which the graph declares as the initial value of an
/// <c>iState_&lt;MOVT&gt;</c> variable in the project's root behaviour file. It
/// is not a state machine id.
/// </remarks>
public sealed class SpeedEntry
{
    /// <summary>The locomotion state id this table answers for.</summary>
    public uint Key { get; set; }

    /// <summary>The per-heading curves. 19 in every well-formed entry.</summary>
    public List<SpeedRecord> Records { get; set; } = [];

    /// <summary>
    /// Whether this entry describes nothing.
    /// </summary>
    /// <remarks>
    /// Two of the shipped 88 are malformed this way, both in the Falmer's block,
    /// and a reader must tolerate them rather than treat the file as corrupt.
    /// </remarks>
    public bool IsEmpty => Records.Count == 0;

    /// <summary>
    /// Picks the curve for a heading and reads it.
    /// </summary>
    /// <remarks>
    /// Takes the record whose <see cref="SpeedRecord.Direction"/> is nearest
    /// <paramref name="direction"/>, which handles I5 correctly -- a heading in
    /// [0.95, 1.0) has no record and falls to 0.90.
    ///
    /// <strong>Nearest-record is an assumption.</strong> The query is known to
    /// take a float direction and return a float, but whether the engine snaps
    /// to a record or interpolates between two adjacent ones has not been read
    /// out of the binary. Snapping is the conservative reading: it cannot
    /// invent a value between two curves. If it turns out to interpolate, this
    /// is where that changes, and only headings off the 0.05 grid are affected.
    ///
    /// Returns <paramref name="goalSpeed"/> unchanged when the entry is empty.
    /// </remarks>
    public float Sample(float direction, float goalSpeed)
    {
        SpeedRecord? best = null;
        float bestDelta = float.MaxValue;

        foreach (SpeedRecord record in Records)
        {
            float delta = Math.Abs(record.Direction - direction);
            if (delta >= bestDelta) continue;
            bestDelta = delta;
            best = record;
        }

        return best?.Sample(goalSpeed) ?? goalSpeed;
    }
}

/// <summary>
/// One project's speed tables, one entry per sampled locomotion state.
/// </summary>
public sealed class SpeedProjectBlock
{
    /// <summary>
    /// The block format version. Every one of the 49 shipped blocks says 1, so
    /// nothing else has been seen and nothing else is written.
    /// </summary>
    public uint Version { get; set; } = 1;

    /// <summary>The sampled states, in file order.</summary>
    public List<SpeedEntry> Entries { get; set; } = [];

    /// <summary>Finds the table for a locomotion state.</summary>
    public SpeedEntry? Entry(uint key) => Entries.FirstOrDefault(e => e.Key == key);
}

/// <summary>
/// <c>speeddatasinglefile.txt</c>: the third file of the animation cache.
/// </summary>
/// <remarks>
/// A binary file despite the name -- an ASCII dirlist, then 32-bit words from
/// byte 1943 on. It answers <c>(state, direction, goal speed) -&gt; speed</c> for
/// every actor, which is how the engine finds out what a creature will actually
/// do when it is asked to move at a given speed.
///
/// The gate <c>bUseSpeedSampler:Animation</c> defaults to 1, so this data is live
/// for every actor whose behaviour graph carries <c>BSSpeedSamplerModifier</c>.
///
/// Read-then-write is byte-exact, as with the other two cache files. The format
/// and everything known about the contents are in <c>docs/speed-data.md</c>.
/// </remarks>
public sealed class SpeedDataFile
{
    /// <summary>The merged file's name.</summary>
    public const string FileName = "speeddatasinglefile.txt";

    /// <summary>
    /// The project list at the head of the file, verbatim.
    /// </summary>
    /// <remarks>
    /// Each line is <c>&lt;Project&gt;Data\&lt;Project&gt;.spd</c>. The blocks
    /// follow in this order with no index and no length prefix, so the list is
    /// what names them: <see cref="Blocks"/>[i] belongs to <see cref="Projects"/>[i].
    /// </remarks>
    public List<string> Projects { get; set; } = [];

    /// <summary>The per-project blocks, in <see cref="Projects"/> order.</summary>
    public List<SpeedProjectBlock> Blocks { get; set; } = [];

    /// <summary>The project stems, with the <c>Data\....spd</c> decoration removed.</summary>
    public IEnumerable<string> ProjectNames => Projects.Select(StemOf);

    /// <summary>Finds a project's block by name, with or without the decoration.</summary>
    public SpeedProjectBlock? Block(string projectName)
    {
        for (int i = 0; i < Projects.Count && i < Blocks.Count; i++)
            if (string.Equals(StemOf(Projects[i]), projectName, StringComparison.OrdinalIgnoreCase))
                return Blocks[i];

        return null;
    }

    /// <summary>
    /// Answers the query the engine makes, for one project.
    /// </summary>
    /// <remarks>
    /// Returns <paramref name="goalSpeed"/> unchanged when the project or the
    /// state is absent, which is the engine's own behaviour with no database:
    /// the modifier passes the request through and the gait blend is indexed by
    /// the requested speed rather than the achievable one.
    /// </remarks>
    public float Sample(string projectName, uint state, float direction, float goalSpeed) =>
        Block(projectName)?.Entry(state)?.Sample(direction, goalSpeed) ?? goalSpeed;

    /// <summary>Strips <c>Data\&lt;name&gt;.spd</c> down to the project stem.</summary>
    public static string StemOf(string listing)
    {
        ArgumentNullException.ThrowIfNull(listing);

        int slash = listing.IndexOf('\\');
        string head = slash < 0 ? listing : listing[..slash];
        return head.EndsWith("Data", StringComparison.Ordinal) ? head[..^4] : head;
    }

    /// <summary>Builds the dirlist line for a project stem.</summary>
    public static string ListingFor(string projectStem) => $@"{projectStem}Data\{projectStem}.spd";

    public static SpeedDataFile Load(string path) => Parse(File.ReadAllBytes(path));

    /// <summary>Reads the file.</summary>
    /// <exception cref="InvalidDataException">The bytes are not this format.</exception>
    public static SpeedDataFile Parse(ReadOnlySpan<byte> bytes)
    {
        var file = new SpeedDataFile();
        int pos = 0;

        int count = int.Parse(ReadLine(bytes, ref pos), CultureInfo.InvariantCulture);
        if (count < 0) throw new InvalidDataException($"negative project count {count}");

        for (int i = 0; i < count; i++) file.Projects.Add(ReadLine(bytes, ref pos));

        for (int i = 0; i < count; i++)
        {
            var block = new SpeedProjectBlock { Version = ReadU32(bytes, ref pos) };
            int entries = checked((int)ReadU32(bytes, ref pos));

            for (int e = 0; e < entries; e++)
            {
                var entry = new SpeedEntry { Key = ReadU32(bytes, ref pos) };
                int records = checked((int)ReadU32(bytes, ref pos));

                for (int r = 0; r < records; r++)
                {
                    var record = new SpeedRecord { Direction = ReadF32(bytes, ref pos) };
                    int points = checked((int)ReadU32(bytes, ref pos));

                    record.Points.Capacity = points;
                    for (int p = 0; p < points; p++)
                        record.Points.Add(new SpeedPoint(ReadF32(bytes, ref pos), ReadF32(bytes, ref pos)));

                    entry.Records.Add(record);
                }

                block.Entries.Add(entry);
            }

            file.Blocks.Add(block);
        }

        if (pos != bytes.Length)
            throw new InvalidDataException($"{bytes.Length - pos} trailing bytes after the last block");

        return file;
    }

    /// <summary>Writes the file.</summary>
    /// <remarks>
    /// Reproduces the shipped bytes exactly for a file that was read and not
    /// edited. The dirlist is ASCII with CRLF endings; everything after it is
    /// little-endian 32-bit words with no padding.
    /// </remarks>
    public byte[] Write()
    {
        if (Projects.Count != Blocks.Count)
            throw new InvalidOperationException(
                $"{Projects.Count} projects listed but {Blocks.Count} blocks: the dirlist names the blocks");

        // The merged loader parses the count line with radix 10 into a u16, so a
        // longer list would be read back wrong by the game however well it writes.
        if (Projects.Count > ushort.MaxValue)
            throw new InvalidOperationException(
                $"{Projects.Count} projects: the game reads the dirlist count as a u16, so at most {ushort.MaxValue}");

        var header = new StringBuilder();
        header.Append(Projects.Count.ToString(CultureInfo.InvariantCulture)).Append(CacheText.NewLine);
        foreach (string listing in Projects) header.Append(listing).Append(CacheText.NewLine);

        byte[] dirlist = Encoding.ASCII.GetBytes(header.ToString());

        var body = new List<byte>(capacity: 1 << 18);
        foreach (SpeedProjectBlock block in Blocks)
        {
            WriteU32(body, block.Version);
            WriteU32(body, (uint)block.Entries.Count);

            foreach (SpeedEntry entry in block.Entries)
            {
                WriteU32(body, entry.Key);
                WriteU32(body, (uint)entry.Records.Count);

                foreach (SpeedRecord record in entry.Records)
                {
                    WriteF32(body, record.Direction);
                    WriteU32(body, (uint)record.Points.Count);

                    foreach (SpeedPoint point in record.Points)
                    {
                        WriteF32(body, point.X);
                        WriteF32(body, point.Y);
                    }
                }
            }
        }

        var outp = new byte[dirlist.Length + body.Count];
        dirlist.CopyTo(outp, 0);
        body.CopyTo(outp, dirlist.Length);
        return outp;
    }

    public void Save(string path) => File.WriteAllBytes(path, Write());

    private static string ReadLine(ReadOnlySpan<byte> bytes, ref int pos)
    {
        int start = pos;
        while (pos < bytes.Length && bytes[pos] != (byte)'\n') pos++;
        if (pos >= bytes.Length) throw new InvalidDataException("the dirlist ends without a newline");

        int end = pos;
        if (end > start && bytes[end - 1] == (byte)'\r') end--;

        pos++;
        return Encoding.ASCII.GetString(bytes[start..end]);
    }

    private static uint ReadU32(ReadOnlySpan<byte> bytes, ref int pos)
    {
        Need(bytes, pos, 4);
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(bytes[pos..]);
        pos += 4;
        return value;
    }

    private static float ReadF32(ReadOnlySpan<byte> bytes, ref int pos)
    {
        Need(bytes, pos, 4);
        float value = BinaryPrimitives.ReadSingleLittleEndian(bytes[pos..]);
        pos += 4;
        return value;
    }

    private static void Need(ReadOnlySpan<byte> bytes, int pos, int n)
    {
        if (pos + n > bytes.Length)
            throw new InvalidDataException($"the file ends {pos + n - bytes.Length} bytes into a {n}-byte field at {pos}");
    }

    private static void WriteU32(List<byte> outp, uint value)
    {
        Span<byte> word = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(word, value);
        outp.AddRange(word);
    }

    private static void WriteF32(List<byte> outp, float value)
    {
        Span<byte> word = stackalloc byte[4];
        BinaryPrimitives.WriteSingleLittleEndian(word, value);
        outp.AddRange(word);
    }
}
