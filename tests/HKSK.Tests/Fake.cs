using System.Numerics;
using HKSK.Cache;
using HKSK.Havok;
using HKSK.Model;

namespace HKSK.Tests;

/// <summary>
/// A small project built in memory, for tests about editing.
/// </summary>
/// <remarks>
/// Three animations, three clips, and root motion on two of them:
///
/// <code>
///   slot 0  Animations\Idle.hkx   clip "Idle"   no motion
///   slot 1  Animations\Walk.hkx   clip "Walk"   travels 10
///   slot 2  Animations\Run.hkx    clip "Run"    travels 30
/// </code>
///
/// Small enough that a wrong index is obvious, and shaped like the real thing:
/// a slot with no motion, and motion blocks that are not in slot order until
/// something sorts them.
/// </remarks>
public static class Fake
{
    public static AnimationDataProject Data()
    {
        var block = new ProjectBlock
        {
            HasFiles = true,
            Files =
            [
                "Behaviors\\FakeBehavior.hkx",
                "Characters\\Fake.hkx",
                "Character Assets\\skeleton.hkx",
            ],
            HasAnimationCache = true,
            Clips =
            [
                Clip("Idle", 0),
                Clip("Walk", 1),
                Clip("Run", 2),
            ],
        };

        var movements = new ProjectDataBlock
        {
            Movements = [Motion(1, 10f), Motion(2, 30f)],
        };

        return new AnimationDataProject
        {
            Name = "FakeProject.txt",
            Block = block,
            Movements = movements,
        };
    }

    /// <summary>The same project with a character file, so indices can be edited.</summary>
    public static ActorProject Project() =>
        ActorProject.Open(
            Data(),
            CharacterFile.Create("Fake",
            [
                "Animations\\Idle.hkx",
                "Animations\\Walk.hkx",
                "Animations\\Run.hkx",
            ]));

    private static ClipGeneratorEntry Clip(string name, int index) => new()
    {
        Name = name,
        CacheIndex = index,
        PlaybackSpeed = 1f,
    };

    private static ClipMovement Motion(int index, float distance) => new()
    {
        CacheIndex = index,
        Duration = 1f,
        Translations =
        [
            new TranslationKey(0f, Vector3.Zero),
            new TranslationKey(1f, new Vector3(distance, 0f, 0f)),
        ],
        Rotations = [new RotationKey(0f, Quaternion.Identity)],
    };
}
