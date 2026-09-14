using HKSK.Cache;
using HKSK.Model;
using Xunit;

namespace HKSK.Tests;

/// <summary>
/// The speed sampler file, read and written.
/// </summary>
/// <remarks>
/// The bar is the same as for the other two cache files: reproducing Bethesda's
/// own bytes, not merely parsing to a model that round-trips through itself.
/// This one is binary, so the check is over bytes rather than text, and the
/// float fields make it strict -- any value the reader mangles shows up.
/// </remarks>
public class SpeedDataTests
{
    [CorpusFact]
    public void TheMergedSpeedDataSurvivesByteForByte()
    {
        byte[] original = File.ReadAllBytes(Corpus.SpeedData);
        byte[] written = SpeedDataFile.Parse(original).Write();

        Assert.Equal(original.Length, written.Length);

        for (int i = 0; i < original.Length; i++)
            if (original[i] != written[i])
                Assert.Fail($"byte {i} differs: {original[i]:X2} became {written[i]:X2}");
    }

    /// <summary>
    /// The counts the format section quotes, checked against the file.
    /// </summary>
    /// <remarks>
    /// Exact numbers on purpose. They are what the documented size arithmetic
    /// rests on, and if the shipped file ever differs from them the doc is wrong
    /// rather than the reader.
    /// </remarks>
    [CorpusFact]
    public void TheShippedFileHasTheDocumentedShape()
    {
        SpeedDataFile file = SpeedDataFile.Load(Corpus.SpeedData);

        Assert.Equal(49, file.Projects.Count);
        Assert.Equal(49, file.Blocks.Count);
        Assert.All(file.Blocks, b => Assert.Equal(1u, b.Version));

        int entries = file.Blocks.Sum(b => b.Entries.Count);
        int records = file.Blocks.Sum(b => b.Entries.Sum(e => e.Records.Count));
        int points = file.Blocks.Sum(b => b.Entries.Sum(e => e.Records.Sum(r => r.Points.Count)));

        Assert.Equal(88, entries);
        Assert.Equal(1634, records);
        Assert.Equal(18302, points);

        // sizeof: 8 bytes of header per block, entry and record; 8 per point.
        long body = (file.Blocks.Count + entries + records + points) * 8L;
        Assert.Equal(160584, body);
    }

    /// <summary>
    /// Two of the 88 entries describe nothing, and a reader must not choke.
    /// </summary>
    /// <remarks>
    /// Both are the Falmer's. One key is INT_MIN; the other is 0x626E7572,
    /// which is not a number at all -- its bytes are the ASCII "runb", the head
    /// of a clip name that an exporter wrote into a u32 field.
    /// </remarks>
    [CorpusFact]
    public void TheFalmersMalformedEntriesAreTolerated()
    {
        SpeedDataFile file = SpeedDataFile.Load(Corpus.SpeedData);
        SpeedProjectBlock falmer = Assert.IsType<SpeedProjectBlock>(file.Block("FalmerProject"));

        Assert.Equal(4, falmer.Entries.Count);
        Assert.Equal(2, falmer.Entries.Count(e => e.IsEmpty));

        Assert.Contains(falmer.Entries, e => e.Key == 0x80000000u);
        Assert.Contains(falmer.Entries, e => e.Key == 0x626E7572u);

        // The junk keys are the empty ones; the real states 1 and 2 are intact.
        Assert.All(falmer.Entries.Where(e => e.Key is 1 or 2), e => Assert.Equal(19, e.Records.Count));
    }

    /// <summary>Every populated entry carries the 19 headings, 0.00 to 0.90.</summary>
    [CorpusFact]
    public void PopulatedEntriesCarryNineteenHeadings()
    {
        SpeedDataFile file = SpeedDataFile.Load(Corpus.SpeedData);
        float[] expected = [.. SpeedRecord.StandardDirections()];

        Assert.Equal(19, expected.Length);

        foreach (SpeedEntry entry in file.Blocks.SelectMany(b => b.Entries).Where(e => !e.IsEmpty))
        {
            Assert.Equal(19, entry.Records.Count);

            // Bit-exact: the file stores an accumulation of +0.05f, which is not
            // 0.05f * i. The two diverge from i = 7.
            for (int i = 0; i < 19; i++)
                Assert.Equal(expected[i], entry.Records[i].Direction);
        }
    }

    /// <summary>
    /// Sampling reproduces the engine's lookup: clamp, else interpolate.
    /// </summary>
    [Fact]
    public void SamplingClampsAtBothEndsAndInterpolatesBetween()
    {
        var record = new SpeedRecord
        {
            Direction = 0f,
            Points = [new SpeedPoint(10f, 100f), new SpeedPoint(20f, 200f)],
        };

        Assert.Equal(100f, record.Sample(0f));      // below the first point
        Assert.Equal(100f, record.Sample(10f));
        Assert.Equal(150f, record.Sample(15f));     // interpolated
        Assert.Equal(200f, record.Sample(20f));
        Assert.Equal(200f, record.Sample(999f));    // above the last
    }

    /// <summary>
    /// An absent project, state or curve returns the request unchanged.
    /// </summary>
    /// <remarks>
    /// That is what the engine does with no database at all: the modifier is
    /// skipped and speedOut is left holding goalSpeed. A reader that returned
    /// zero instead would model a creature that cannot move.
    /// </remarks>
    [Fact]
    public void AnAbsentTablePassesTheRequestThrough()
    {
        var file = new SpeedDataFile();

        Assert.Equal(275f, file.Sample("NoSuchProject", 0, 0f, 275f));
        Assert.Equal(275f, new SpeedEntry().Sample(0f, 275f));
        Assert.Equal(275f, new SpeedRecord().Sample(275f));
    }

    [Fact]
    public void TheDirlistDecorationRoundTrips()
    {
        Assert.Equal("DeerProject", SpeedDataFile.StemOf(@"DeerProjectData\DeerProject.spd"));
        Assert.Equal("DeerProjectData\\DeerProject.spd", SpeedDataFile.ListingFor("DeerProject"));
        Assert.Equal("DeerProject", SpeedDataFile.StemOf(SpeedDataFile.ListingFor("DeerProject")));
    }

    [Fact]
    public void WritingRefusesAMismatchedDirlist()
    {
        var file = new SpeedDataFile { Projects = ["AData\\A.spd"] };

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => file.Write());
        Assert.Contains("dirlist names the blocks", error.Message);
    }

    [Fact]
    public void TruncatedBytesAreRejected()
    {
        var file = new SpeedDataFile
        {
            Projects = [SpeedDataFile.ListingFor("A")],
            Blocks = [new SpeedProjectBlock { Entries = [new SpeedEntry { Key = 7 }] }],
        };

        byte[] whole = file.Write();
        Assert.Throws<InvalidDataException>(() => SpeedDataFile.Parse(whole.AsSpan(0, whole.Length - 2)));
    }

    /// <summary>A synthetic file survives its own round trip, corpus or not.</summary>
    [Fact]
    public void ASyntheticFileRoundTrips()
    {
        var file = new SpeedDataFile
        {
            Projects = [SpeedDataFile.ListingFor("TestProject")],
            Blocks =
            [
                new SpeedProjectBlock
                {
                    Entries =
                    [
                        new SpeedEntry
                        {
                            Key = 20,
                            Records = [.. SpeedRecord.StandardDirections().Select(d => new SpeedRecord
                            {
                                Direction = d,
                                Points = [new SpeedPoint(0f, 4.92f), new SpeedPoint(324.5f, 431.92f)],
                            })],
                        },
                        new SpeedEntry { Key = 21 },   // the malformed shape, deliberately
                    ],
                },
            ],
        };

        byte[] written = file.Write();
        SpeedDataFile reread = SpeedDataFile.Parse(written);

        Assert.Equal(written, reread.Write());
        Assert.Equal("TestProject", Assert.Single(reread.ProjectNames));
        Assert.Equal(19, reread.Block("TestProject")!.Entry(20)!.Records.Count);
        Assert.True(reread.Block("TestProject")!.Entry(21)!.IsEmpty);
    }

    /// <summary>The cache picks the file up from the meshes folder.</summary>
    [CorpusFact]
    public void TheCacheLoadsSpeedDataFromTheMeshesFolder()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);

        Assert.NotNull(cache.SpeedData);
        Assert.Equal(49, cache.SpeedData!.Blocks.Count);
        Assert.Contains("DeerProject", cache.SpeedData.ProjectNames);
    }
}
