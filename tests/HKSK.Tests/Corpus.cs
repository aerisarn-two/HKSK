using Xunit;

namespace HKSK.Tests;

/// <summary>
/// Locates the game data the corpus tests read.
/// </summary>
/// <remarks>
/// The cache files and the .hkx they describe are extracted game data: they
/// cannot live in this repository or reach a runner, so they come from
/// HKSK_CORPUS. Tests that need them skip when it is unset, and the rest of the
/// suite still runs.
///
/// Set it to the meshes directory, e.g.
///     HKSK_CORPUS=~/Dev/BSAFileExtractor/extracted/meshes dotnet test
/// </remarks>
public static class Corpus
{
    public const string EnvVar = "HKSK_CORPUS";

    public static string? Root
    {
        get
        {
            string? value = Environment.GetEnvironmentVariable(EnvVar);
            if (string.IsNullOrWhiteSpace(value)) return null;

            string expanded = value.StartsWith("~/")
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), value[2..])
                : value;

            return Directory.Exists(expanded) ? expanded : null;
        }
    }

    public static bool Available => Root is not null;

    public static string Path_(params string[] parts) =>
        Path.Combine([Root ?? throw new InvalidOperationException("no corpus"), .. parts]);

    /// <summary>The merged animation data file.</summary>
    public static string AnimationData => Path_("animationdatasinglefile.txt");

    /// <summary>The merged animation set data file.</summary>
    public static string AnimationSetData => Path_("animationsetdatasinglefile.txt");
}

/// <summary>Skips a test when there is no corpus to run it against.</summary>
public sealed class CorpusFactAttribute : FactAttribute
{
    public CorpusFactAttribute()
    {
        if (!Corpus.Available)
            Skip = $"set {Corpus.EnvVar} to an extracted meshes directory to run this";
    }
}

/// <summary>As <see cref="CorpusFactAttribute"/>, for a theory.</summary>
public sealed class CorpusTheoryAttribute : TheoryAttribute
{
    public CorpusTheoryAttribute()
    {
        if (!Corpus.Available)
            Skip = $"set {Corpus.EnvVar} to an extracted meshes directory to run this";
    }
}
