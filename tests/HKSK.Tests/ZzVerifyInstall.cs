using System.Text;
using HKSK.Cache;
using HKSK.Model;
using Xunit;

namespace HKSK.Tests;

// The installed creature, read back the way the game reads one: found by project name in the
// merged cache, its root behaviour taken as the first file in its list.
public sealed class ZzVerifyInstall
{
    [Fact]
    public void Dump()
    {
        string mod = Environment.GetEnvironmentVariable("HKSK_MOD") ?? "";
        if (!File.Exists(Path.Combine(mod, SkyrimCache.AnimationDataFileName))) return;

        // Never the bare temp root: a skeleton.hkx or a skeleton.nif left there makes every
        // folder-scanning route read the whole of it as a creature.
        string outDir = Environment.GetEnvironmentVariable("HKSK_CENSUS_OUT")
            ?? Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "hksk-creature-dump")).FullName;
        var sb = new StringBuilder();

        SkyrimCache cache = SkyrimCache.Load(mod);
        sb.AppendLine($"{cache.AnimationData.Projects.Count} projects in the cache");

        AnimationDataProject? row = cache.AnimationData.Projects
            .FirstOrDefault(p => p.Stem.Equals("BoneSkeletonProject", StringComparison.OrdinalIgnoreCase));

        if (row is null) { sb.AppendLine("!! not found"); File.WriteAllText(Path.Combine(outDir, "verify.txt"), sb.ToString()); return; }

        sb.AppendLine($"row '{row.Name}'");
        sb.AppendLine($"  files ({row.Block.Files.Count}):");
        foreach (string f in row.Block.Files) sb.AppendLine($"    {f}");
        sb.AppendLine($"  clip generators: {row.Block.Clips.Count}");
        foreach (ClipGeneratorEntry c in row.Block.Clips.Take(30))
            sb.AppendLine($"    [{c.CacheIndex,2}] {c.Name}");
        sb.AppendLine($"  movement rows: {row.Movements?.Movements.Count ?? 0}");
        foreach (ClipMovement m in row.Movements?.Movements ?? [])
        {
            TranslationKey? last = m.Translations.Count > 0 ? m.Translations[^1] : null;
            sb.AppendLine($"    clip {m.CacheIndex,2}: {m.Duration:0.##}s travels " +
                          (last is null ? "nowhere" : $"({last.Value.X:0.#},{last.Value.Y:0.#},{last.Value.Z:0.#})"));
        }

        // The files the row names have to actually be on disk under the meshes folder, or
        // the game reports that the root behaviour cannot be found.
        sb.AppendLine("  on disk?");
        foreach (string f in row.Block.Files)
        {
            string path = Path.Combine(mod, "actors", "boneskeleton", f.Replace('\\', Path.DirectorySeparatorChar));
            sb.AppendLine($"    {(File.Exists(path) ? "yes" : "NO ")} {f}");
        }

        File.WriteAllText(Path.Combine(outDir, "verify.txt"), sb.ToString());
    }
}
