using System.Runtime.CompilerServices;

namespace HKSK.Tests;

/// <summary>
/// Where the investigation harnesses write what they find.
/// </summary>
/// <remarks>
/// <para>
/// The Zz files are records of questions asked of the game's data, kept because the answers
/// are what the library is built on and because the next question is usually a small edit to
/// the last one. They are not assertions and they prove nothing; they print.
/// </para>
/// <para>
/// They wrote to an absolute path belonging to the session that first asked, which worked
/// until that folder was cleared and then failed 57 files at once for a reason that had
/// nothing to do with any of them. So the folder is made before anything runs, here, once.
/// </para>
/// </remarks>
public static class Dumps
{
    /// <summary>
    /// The folder the harnesses write into, which they name literally rather than asking for.
    /// </summary>
    /// <remarks>
    /// A literal, because it is written into 57 files and a shared accessor would have meant
    /// editing each of their call sites rather than one string. The harnesses that take a
    /// folder from the environment read it themselves.
    /// </remarks>
    public const string Folder = "/tmp/hksk-dumps";

    [ModuleInitializer]
    internal static void Ensure() => Directory.CreateDirectory(Folder);
}
