using HKSK.Model;
using HKX2;

namespace HKSK.SetData;

/// <summary>
/// A project's animation files, named the way the checksums name them, and which file a
/// clip plays.
/// </summary>
/// <remarks>
/// A clip's file is <em>not</em> the path its generator stores. The engine binds a clip to
/// a character's animation by the clip's name, through the animation cache, and the file
/// is whatever the character file lists at that index. The difference is real: the male
/// and female characters share one behaviour whose clips say <c>Animations\male\...</c>,
/// and <c>defaultfemale.hkx</c> lists <c>Animations\female\...</c> at the same indices.
/// The stored path is used only for a clip the cache does not list.
/// </remarks>
internal sealed class ProjectFiles
{
    private readonly string _meshes;
    private readonly string _folder;
    private readonly Dictionary<string, List<string>> _byClip = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _order = new(StringComparer.OrdinalIgnoreCase);

    public ProjectFiles(string meshes, ActorProject project)
    {
        _meshes = Path.GetFullPath(meshes);
        _folder = project.Folder!;

        foreach (AnimationSlot slot in project.Animations)
        {
            if (string.IsNullOrWhiteSpace(slot.StoredName)) continue;
            string path = DataPath(slot.StoredName);
            if (_order.TryAdd(path, _paths.Count)) { _paths.Add(path); All.Add(path); }
        }

        foreach (Clip clip in project.Clips)
        {
            if (clip.Slot is null || string.IsNullOrWhiteSpace(clip.Slot.StoredName)) continue;
            if (!_byClip.TryGetValue(clip.Name, out var paths)) _byClip[clip.Name] = paths = [];
            paths.Add(DataPath(clip.Slot.StoredName));
        }
    }

    /// <summary>Every animation the character lists, once each, in its order.</summary>
    public List<string> All { get; } = [];

    /// <summary>Every file known, by index: the character's list, then any a clip names that it does not.</summary>
    private readonly List<string> _paths = [];

    /// <summary>The files a group of clips plays.</summary>
    public FileSet Of(IEnumerable<hkbClipGenerator> clips)
    {
        var set = new FileSet(_paths.Count);

        foreach (hkbClipGenerator clip in clips)
        {
            if (_byClip.TryGetValue(clip.m_name, out var listed))
                foreach (string path in listed) set.Add(IndexOf(path));
            else if (!string.IsNullOrWhiteSpace(clip.m_animationName))
                set.Add(IndexOf(DataPath(clip.m_animationName)));
        }

        return set;
    }

    /// <summary>Every file the character lists, as a set.</summary>
    public FileSet Everything()
    {
        var set = new FileSet(_paths.Count);
        for (int i = 0; i < All.Count; i++) set.Add(i);
        return set;
    }

    /// <summary>The paths of a set, in the character's order, anything it does not list last.</summary>
    public List<string> Paths(FileSet set) => [.. set.Indices().Order().Select(i => _paths[i])];

    private int IndexOf(string path)
    {
        if (_order.TryGetValue(path, out int at)) return at;

        _order[path] = _paths.Count;
        _paths.Add(path);
        return _paths.Count - 1;
    }

    // Data-relative, as the checksums are taken: meshes\actors\...\animations\walk.hkx.
    // A character stores some paths with "..", which has to be collapsed first.
    private string DataPath(string stored)
    {
        string full = Path.GetFullPath(Path.Combine(_folder, stored.Replace('\\', '/')));
        return "meshes\\" + Path.GetRelativePath(_meshes, full).Replace('/', '\\');
    }
}
