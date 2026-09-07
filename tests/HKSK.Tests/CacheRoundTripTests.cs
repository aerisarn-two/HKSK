using HKSK.Cache;
using Xunit;

namespace HKSK.Tests;

/// <summary>
/// The cache files have to come back out exactly as they went in.
/// </summary>
/// <remarks>
/// Byte-for-byte is the bar rather than "parses to the same model", because an
/// editor that rewrites the whole merged file on every save would otherwise
/// churn thousands of unrelated lines -- and because reproducing the game's own
/// bytes is the only evidence that the float formatting, the line counts and the
/// CRLF endings are all right. A reader that agrees with its own writer proves
/// nothing; agreeing with Bethesda's file does.
/// </remarks>
public class CacheRoundTripTests
{
    [CorpusFact]
    public void TheMergedAnimationDataSurvivesByteForByte()
    {
        string original = File.ReadAllText(Corpus.AnimationData);
        string written = AnimationDataFile.Parse(original).Write();

        AssertIdentical(original, written);
    }

    [CorpusFact]
    public void TheMergedAnimationSetDataSurvivesByteForByte()
    {
        string original = File.ReadAllText(Corpus.AnimationSetData);
        string written = AnimationSetDataFile.Parse(original).Write();

        AssertIdentical(original, written);
    }

    /// <summary>
    /// The per-project files are the same grammar without the line counts, so
    /// they exercise the block readers on their own.
    /// </summary>
    [CorpusFact]
    public void EveryProjectFileSurvivesByteForByte()
    {
        string folder = Corpus.Path_("animationdata");
        var failures = new List<string>();
        int checkedFiles = 0;

        foreach (string path in Directory.EnumerateFiles(folder, "*.txt").OrderBy(p => p))
        {
            // Not a project: the merge order, handled by DirList.
            if (Path.GetFileName(path).Equals("dirlist.txt", StringComparison.OrdinalIgnoreCase))
                continue;

            string original = File.ReadAllText(path);
            var w = new LineWriter();

            try
            {
                ProjectBlock.Read(Cursor(original)).Write(w);
            }
            catch (Exception e)
            {
                failures.Add($"{Path.GetFileName(path)}: {e.Message}");
                continue;
            }

            checkedFiles++;
            if (w.ToString() != original) failures.Add($"{Path.GetFileName(path)}: differs");
        }

        Assert.True(checkedFiles > 300, $"expected the corpus to hold the project files, saw {checkedFiles}");
        Assert.Empty(failures);
    }

    [CorpusFact]
    public void EveryRootMotionFileSurvivesByteForByte()
    {
        string folder = Corpus.Path_("animationdata", "boundanims");
        var failures = new List<string>();
        int checkedFiles = 0;

        foreach (string path in Directory.EnumerateFiles(folder, "*.txt").OrderBy(p => p))
        {
            string original = File.ReadAllText(path);
            var w = new LineWriter();

            try
            {
                ProjectDataBlock.Read(Cursor(original)).Write(w);
            }
            catch (Exception e)
            {
                failures.Add($"{Path.GetFileName(path)}: {e.Message}");
                continue;
            }

            checkedFiles++;
            if (w.ToString() != original) failures.Add($"{Path.GetFileName(path)}: differs");
        }

        Assert.True(checkedFiles > 30, $"expected the corpus to hold the root motion files, saw {checkedFiles}");
        Assert.Empty(failures);
    }

    [CorpusFact]
    public void BothDirectoryListingsSurviveByteForByte()
    {
        foreach (string path in new[]
                 {
                     Corpus.Path_("animationdata", "dirlist.txt"),
                     Corpus.Path_("animationsetdata", "dirlist.txt"),
                 })
        {
            string original = File.ReadAllText(path);
            AssertIdentical(original, DirList.Parse(original).Write());
        }
    }

    private static LineCursor Cursor(string text)
    {
        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        if (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        return new LineCursor(lines);
    }

    /// <summary>Reports the first difference rather than dumping megabytes.</summary>
    private static void AssertIdentical(string expected, string actual)
    {
        if (expected == actual) return;

        string[] a = expected.Split('\n');
        string[] b = actual.Split('\n');

        for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
            if (a[i] != b[i])
                Assert.Fail($"line {i + 1} differs:\n  original: '{a[i].TrimEnd('\r')}'\n  written:  '{b[i].TrimEnd('\r')}'");

        Assert.Fail($"same prefix, different length: original {a.Length} lines, written {b.Length}");
    }
}
