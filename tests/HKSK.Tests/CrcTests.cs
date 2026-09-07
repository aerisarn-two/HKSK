using HKSK.Cache;
using Xunit;

namespace HKSK.Tests;

/// <summary>
/// The animation set data names animation files by checksum, so the checksum has
/// to be the one the game computed.
/// </summary>
public class CrcTests
{
    /// <summary>
    /// The chicken's set data is small enough to check by hand: one set,
    /// twenty animations, all in the same folder.
    /// </summary>
    [Theory]
    [InlineData("meshes\\actors\\ambient\\chicken\\animations", 2725300844u)]
    [InlineData("aggrowarning1", 329189360u)]
    [InlineData("idle_fulbody2", 1282959495u)]
    public void TheChecksumMatchesTheOnesTheGameShipped(string text, uint expected) =>
        Assert.Equal(expected, HavokCrc.Compute(text));

    [Fact]
    public void TheChecksumIgnoresCase() =>
        Assert.Equal(HavokCrc.Compute("AggroWarning1"), HavokCrc.Compute("aggrowarning1"));

    [Fact]
    public void ATripleSplitsFolderFromNameAndDropsTheExtension()
    {
        var (folder, name, extension) =
            HavokCrc.Triple("meshes\\actors\\ambient\\chicken\\animations\\AggroWarning1.hkx");

        Assert.Equal("2725300844", folder);
        Assert.Equal("329189360", name);
        Assert.Equal(HavokCrc.ExtensionCode, extension);
    }

    /// <summary>
    /// Every checksum triple in the shipped set data ends with the same constant,
    /// which is what lets <see cref="HavokCrc.ExtensionCode"/> be a constant.
    /// </summary>
    [CorpusFact]
    public void EveryShippedTripleEndsWithTheExtensionCode()
    {
        AnimationSetDataFile sets = AnimationSetDataFile.Load(Corpus.AnimationSetData);

        int triples = 0;
        foreach (AnimationSetDataProject project in sets.Projects)
            foreach (ProjectAttackBlock set in project.Sets.Sets)
                foreach (var (_, _, extension) in set.Checksums.Triples())
                {
                    Assert.Equal(HavokCrc.ExtensionCode, extension);
                    triples++;
                }

        Assert.True(triples > 1000, $"expected the set data to name many animations, saw {triples}");
    }

    /// <summary>
    /// The chicken's checksums are reproduced from the animation files on disk,
    /// which is the real test: it goes from a path to the number the game stored.
    /// </summary>
    [CorpusFact]
    public void TheChickensChecksumsAreReproducedFromItsAnimationFiles()
    {
        AnimationSetDataFile sets = AnimationSetDataFile.Load(Corpus.AnimationSetData);
        AnimationSetDataProject project = Assert.Single(
            sets.Projects, p => p.Stem.Equals("ChickenProject", StringComparison.OrdinalIgnoreCase));

        ProjectAttackBlock set = Assert.Single(project.Sets.Sets);
        var shipped = set.Checksums.Triples().Select(t => (t.Folder, t.Name)).ToHashSet();

        string folder = Corpus.Path_("actors", "ambient", "chicken", "animations");
        var computed = Directory.EnumerateFiles(folder, "*.hkx")
            .Select(f => HavokCrc.Triple(
                $"meshes\\actors\\ambient\\chicken\\animations\\{Path.GetFileName(f)}"))
            .Select(t => (t.Folder, t.Name))
            .ToHashSet();

        // Every animation the set names must be one that exists on disk.
        Assert.Empty(shipped.Except(computed));
        Assert.Equal(20, shipped.Count);
    }
}
