using HKX2;

namespace HKSK.Havok;

/// <summary>
/// A loaded Havok packfile, with its objects flattened for lookup.
/// </summary>
/// <remarks>
/// HKX2 hands back a root container and leaves the graph to the caller. Almost
/// everything wanted here -- the clip generators of a behaviour, the character
/// data of a character file -- is somewhere below that root without a fixed
/// path, so the graph is walked once on load and the objects kept.
/// </remarks>
public sealed class HavokFile
{
    private readonly List<IHavokObject> _objects = [];

    private HavokFile(string path, hkRootLevelContainer root)
    {
        Path = path;
        Root = root;
        Collect(root, new HashSet<object>(ReferenceEqualityComparer.Instance));
    }

    /// <summary>Where the file was read from.</summary>
    public string Path { get; }

    /// <summary>The root container, for writing back.</summary>
    public hkRootLevelContainer Root { get; }

    /// <summary>Every object reachable from the root, root first.</summary>
    public IReadOnlyList<IHavokObject> Objects => _objects;

    /// <summary>Wraps a container built in memory, for a file about to be written.</summary>
    /// <remarks>
    /// The assembly route makes its packfiles rather than reading them, and everything
    /// that walks a file -- the editor, the validation, the engine -- takes one of
    /// these. So a graph being built is the same kind of thing as a graph being read.
    /// </remarks>
    public static HavokFile Of(hkRootLevelContainer root, string path = "") => new(path, root);

    public static HavokFile Load(string path)
    {
        if (Util.ReadHKX(path) is not hkRootLevelContainer root)
            throw new InvalidDataException($"'{path}' is not a Havok root level container");

        return new HavokFile(path, root);
    }

    /// <summary>Every object of the given type, in traversal order.</summary>
    public IEnumerable<T> All<T>() where T : class => _objects.OfType<T>();

    /// <summary>The first object of the given type, or null.</summary>
    public T? First<T>() where T : class => _objects.OfType<T>().FirstOrDefault();

    /// <summary>Saves the file back, as a Skyrim SE packfile.</summary>
    public void Save(string path)
    {
        using FileStream stream = File.Create(path);
        Util.WriteHKX(Root, HKXHeader.SkyrimSE(), stream);
    }

    private void Collect(object? node, HashSet<object> seen)
    {
        if (node is null || !seen.Add(node)) return;
        if (node is IHavokObject havok) _objects.Add(havok);

        foreach (var property in node.GetType().GetProperties())
        {
            if (!property.CanRead || property.GetIndexParameters().Length > 0) continue;

            object? value;
            try { value = property.GetValue(node); }
            catch { continue; }

            switch (value)
            {
                case string:
                    break;
                case IHavokObject child:
                    Collect(child, seen);
                    break;
                case System.Collections.IEnumerable items:
                    foreach (object? item in items)
                        if (item is IHavokObject element) Collect(element, seen);
                    break;
            }
        }
    }
}
