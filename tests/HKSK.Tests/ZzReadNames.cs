using System.Text;
using HKSK.Assembly;
using HKSK.Model;
using Xunit;
namespace HKSK.Tests;

// How much of the game's own naming the reader understands, creature by creature.
public sealed class ZzReadNames
{
    [Fact]
    public void Measure()
    {
        string meshes = Environment.GetEnvironmentVariable("HKSK_CORPUS") ?? "";
        string outDir = Environment.GetEnvironmentVariable("HKSK_CENSUS_OUT") ?? Path.GetTempPath();
        if (meshes.Length == 0 || !Directory.Exists(meshes)) return;

        SkyrimCache cache;
        try { cache = SkyrimCache.Load(meshes); } catch { return; }

        var sb = new StringBuilder();
        var unread = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int total = 0, known = 0;

        foreach (ActorProject actor in cache.Actors())
        {
            int here = 0, got = 0;
            foreach (var animation in actor.Animations)
            {
                string stem = Path.GetFileNameWithoutExtension(animation.StoredName.Replace('\\', '/'));
                here++;
                if (RoleReader.Of(stem).Roles.Count > 0) got++;
                else unread[stem] = unread.GetValueOrDefault(stem) + 1;
            }

            total += here; known += got;
            if (here > 0)
                sb.AppendLine($"{actor.Name,-34} {got,5} of {here,5} read  {(float)got / here:P0}");
        }

        sb.AppendLine();
        sb.AppendLine($"== {known} of {total} names read, {(float)known / Math.Max(total, 1):P1}");
        sb.AppendLine("== the commonest it does not know:");
        foreach ((string name, int count) in unread.OrderByDescending(p => p.Value).Take(25))
            sb.AppendLine($"   {name,-40} {count}");

        File.WriteAllText(Path.Combine(outDir, "read_names.txt"), sb.ToString());
    }
}
