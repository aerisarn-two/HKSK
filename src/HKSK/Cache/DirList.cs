using System.Text;

namespace HKSK.Cache;

/// <summary>
/// <c>dirlist.txt</c>: the order the split cache files are merged in.
/// </summary>
/// <remarks>
/// A bare list of names, one per line, with no count and no blocks. It exists
/// because the folder it sits in has no inherent order and the merged files do:
/// the project list at the head of <c>animationdatasinglefile.txt</c> follows
/// this order, and so does <c>animationsetdatasinglefile.txt</c>.
///
/// It matters for editing because a project's position in that list is what its
/// blocks are matched against. Adding a project means adding it here too.
/// </remarks>
public sealed class DirList
{
    public List<string> Entries { get; set; } = [];

    public static DirList Load(string path) => Parse(File.ReadAllText(path));

    public static DirList Parse(string text)
    {
        var list = new DirList();

        foreach (string line in text.Split('\n'))
        {
            string entry = line.TrimEnd('\r');
            if (entry.Length > 0) list.Entries.Add(entry);
        }

        return list;
    }

    public string Write()
    {
        var sb = new StringBuilder();
        foreach (string entry in Entries) sb.Append(entry).Append(CacheText.NewLine);
        return sb.ToString();
    }

    public void Save(string path) => File.WriteAllText(path, Write());

    public bool Contains(string entry) =>
        Entries.Any(e => string.Equals(e, entry, StringComparison.OrdinalIgnoreCase));
}
