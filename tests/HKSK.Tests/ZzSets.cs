using HKSK.Cache;
using HKSK.Model;
using Xunit;

namespace HKSK.Tests;

public sealed class ZzSets
{
    // the extracted corpus is lower-case on a case-sensitive filesystem
    static string? Resolve(string root, string relative)
    {
        string current = root;
        foreach (string part in relative.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == "..") { current = Path.GetDirectoryName(current)!; continue; }
            if (part == ".") continue;
            if (!Directory.Exists(current)) return null;
            string? hit = Directory.EnumerateFileSystemEntries(current).FirstOrDefault(e => string.Equals(Path.GetFileName(e), part, StringComparison.OrdinalIgnoreCase));
            if (hit is null) return null;
            current = hit;
        }
        return current;
    }

    [CorpusFact]
    public void Look()
    {
        string root = Corpus.Root!;
        SkyrimCache cache = SkyrimCache.Load(root);
        var sets = AnimationSetDataFile.Load(Path.Combine(root, "animationsetdatasinglefile.txt"));
        var L = new List<string>();

        // decoder: every folder and every hkx stem under meshes
        var folders = new Dictionary<string, string>();
        foreach (string d in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories).Prepend(root))
        {
            string rel = "meshes" + (d.Length > root.Length ? "\\" + Path.GetRelativePath(root, d).Replace('/', '\\') : "");
            folders.TryAdd(HavokCrc.Text(rel), rel);
        }
        var stems = new Dictionary<string, HashSet<string>>();
        foreach (string f in Directory.EnumerateFiles(root, "*.hkx", SearchOption.AllDirectories))
        {
            string stem = Path.GetFileNameWithoutExtension(f);
            string c = HavokCrc.Text(stem);
            if (!stems.TryGetValue(c, out var s)) stems[c] = s = new(StringComparer.OrdinalIgnoreCase);
            s.Add(stem);
        }
        foreach (var proj0 in cache.OpenAll().OfType<ActorProject>())
            foreach (string a0 in proj0.Character?.AnimationNames ?? [])
            {
                string st0 = Path.GetFileNameWithoutExtension(a0.Replace('\\', '/'));
                string c0 = HavokCrc.Text(st0);
                if (!stems.TryGetValue(c0, out var s0)) stems[c0] = s0 = new(StringComparer.OrdinalIgnoreCase);
                s0.Add(st0 + "(listed)");
            }
        int triples = 0, folderKnown = 0, nameKnown = 0, fileExists = 0;
        foreach (var p in sets.Projects)
            foreach (var set in p.Sets.Sets)
                foreach (var (fo, na, ex) in set.Checksums.Triples())
                {
                    triples++;
                    bool fk = folders.TryGetValue(fo, out var folder);
                    bool nk = stems.TryGetValue(na, out var names);
                    if (fk) folderKnown++;
                    if (nk) nameKnown++;
                    if (fk && nk && !names!.Any(n => !n.EndsWith("(listed)") && Resolve(root, folder![7..] + "\\" + n + ".hkx") is not null))
                        File.AppendAllText("/tmp/notondisk.txt", $"{p.Stem} {folder}\\{string.Join("|", names)}\n");
                    if (fk && nk && names!.Any(n => File.Exists(Path.Combine(root, folder!.Substring(Math.Min(7, folder.Length)).Replace('\\', '/'), n + ".hkx")))) fileExists++;
                }
        L.Add($"decoder: {folders.Count} folders, {stems.Count} stems; triples {triples}, folder decoded {folderKnown}, name decoded {nameKnown}, resolves to an existing file {fileExists}");

        var fpActor = cache.OpenActor("FirstPerson") as ActorProject;
        string fpProj = cache.FindProjectFile("FirstPerson")!;
        var fpUses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string a in fpActor!.Character!.AnimationNames)
            fpUses.Add("meshes\\" + Path.GetFullPath(Path.Combine("/x", Path.GetRelativePath(root, Path.GetDirectoryName(fpProj)!), a.Replace('\\', '/')))[3..].Replace('/', '\\'));
        var fpPlayed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var b in fpActor.Behaviors)
            foreach (var st in HKSK.Behavior.ProjectWalk.Of(fpProj).Steps)
            {
                string? an = st.Node switch { HKX2.hkbClipGenerator c => c.m_animationName, HKX2.BSSynchronizedClipGenerator sc => (sc.m_pClipGenerator as HKX2.hkbClipGenerator)?.m_animationName, _ => null };
                if (an is not null) fpPlayed.Add(Path.GetFileNameWithoutExtension(an.Replace('\\', '/')));
            }
        var fpOrder = fpActor.Character!.AnimationNames
            .Select(a => "meshes\\" + Path.GetFullPath(Path.Combine("/x", Path.GetRelativePath(root, Path.GetDirectoryName(fpProj)!), a.Replace('\\', '/')))[3..].Replace('/', '\\')).ToList();
        L.Add("\n== one-set projects: set vs character animation list");
        foreach (var p in sets.Projects.Where(p => p.Sets.Sets.Count == 1))
        {
            var actor = cache.OpenActor(p.Stem) as ActorProject;
            string? proj = cache.FindProjectFile(p.Stem);
            if (actor?.Character is null || proj is null) { L.Add($"{p.Stem}: no character"); continue; }
            string projDir = "meshes\\" + Path.GetRelativePath(root, Path.GetDirectoryName(proj)!).Replace('/', '\\');
            var expected = new List<(string Folder, string Name)>();
            var missing = new List<string>();
            var firstPerson = new List<(string, string)>();
            var fpPaths = new List<string>();
            foreach (string a in actor.Character.AnimationNames)
            {
                // resolve against the project folder, collapsing "..", case-insensitively
                string projRel = Path.GetRelativePath(root, Path.GetDirectoryName(proj)!);
                string combined = Path.GetFullPath(Path.Combine("/x", projRel, a.Replace('\\', '/')))[3..];
                string rel = "meshes\\" + combined.Replace('/', '\\');
                bool exists = Resolve(root, combined) is not null;
                if (Environment.GetEnvironmentVariable("SKIPMISSING") is not null && !exists) { missing.Add(a); continue; }
                if (Environment.GetEnvironmentVariable("USEDONLY") is not null)
                {
                    string stemA = Path.GetFileNameWithoutExtension(a.Replace('\\', '/'));
                    bool used = actor.Behaviors.SelectMany(b => b.Clips).Any(c => string.Equals(Path.GetFileNameWithoutExtension((c.m_animationName ?? "").Replace('\\', '/')), stemA, StringComparison.OrdinalIgnoreCase))
                             || actor.Clips.Any(c => string.Equals(c.Slot?.StoredName, a, StringComparison.OrdinalIgnoreCase))
                             || a.Contains("SharedKillMoves", StringComparison.OrdinalIgnoreCase);
                    if (!used) { missing.Add("unused:" + a); continue; }
                }
                var t = HavokCrc.Triple(rel);
                expected.Add((t.Folder, t.Name));
                if (rel.Contains("\\sharedkillmoves\\", StringComparison.OrdinalIgnoreCase) && !rel.Contains("\\1stperson\\", StringComparison.OrdinalIgnoreCase))
                {
                    string fp = rel.Replace("\\sharedkillmoves\\", "\\sharedkillmoves\\1stperson\\", StringComparison.OrdinalIgnoreCase);
                    bool keep = Environment.GetEnvironmentVariable("FPRULE") switch
                    {
                        "firstperson" => fpUses.Contains(fp),
                        "played" => fpUses.Contains(fp) && fpPlayed.Contains(Path.GetFileName(fp)),
                        _ => Resolve(root, fp[7..]) is not null,
                    };
                    if (keep)
                    { var u = HavokCrc.Triple(fp); firstPerson.Add((u.Folder, u.Name)); fpPaths.Add(fp); }
                }
            }
            string fpMode = Environment.GetEnvironmentVariable("FIRSTPERSON") ?? "none";
            if (fpMode == "end") expected.AddRange(firstPerson);
            if (fpMode == "fporder")
                expected.AddRange(firstPerson.Zip(fpPaths).OrderBy(z => { int i = fpOrder.FindIndex(o => string.Equals(o, z.Second, StringComparison.OrdinalIgnoreCase)); return i < 0 ? int.MaxValue : i; }).Select(z => z.First));
            var actual = p.Sets.Sets[0].Checksums.Triples().Select(t => (t.Folder, t.Name)).ToList();
            var eSet = expected.ToHashSet(); var aSet = actual.ToHashSet();
            bool sameOrder = expected.SequenceEqual(actual);
            if (!sameOrder)
            {
                string Dec((string Folder, string Name) t) => $"{folders.GetValueOrDefault(t.Folder, "?" + t.Folder)}\\{string.Join("|", stems.GetValueOrDefault(t.Name) ?? ["?" + t.Name])}";
                foreach (var t in aSet.Except(eSet)) L.Add($"      only in set:  {Dec(t)}");
                foreach (var t in eSet.Except(aSet)) L.Add($"      only in char: {Dec(t)}");
                foreach (var g in actual.GroupBy(x => x).Where(g => g.Count() > 1)) L.Add($"      duplicated:   {Dec(g.Key)} x{g.Count()}");
            }
            if (eSet.SetEquals(aSet) && !sameOrder)
                L.Add("      order: set positions in character order: " + string.Join(",", expected.Select(t => actual.IndexOf(t))));
            if (missing.Count > 0) L.Add($"      skipped missing files: {string.Join(", ", missing)}");
            L.Add($"{p.Stem,-30} set {p.Sets.SetFiles[0],-20} anims {actual.Count,4}  character {expected.Count,4}  common {eSet.Intersect(aSet).Count(),4}  onlySet {aSet.Except(eSet).Count(),3}  onlyChar {eSet.Except(aSet).Count(),3}  sameOrder {sameOrder}  dupsInSet {actual.Count - aSet.Count}");
        }
        File.WriteAllText("/tmp/sets.txt", string.Join("\n", L));
    }
}
