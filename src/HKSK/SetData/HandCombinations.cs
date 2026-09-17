namespace HKSK.SetData;

/// <summary>
/// The weapon combinations the game asks the set data about, and how a group of them is
/// written as hand variable ranges.
/// </summary>
/// <remarks>
/// <para>
/// A race builds its attack table by asking for the set of every combination
/// (<c>TESRace</c>'s virtual <c>0x1403de860</c>, loop at <c>0x1403e1e00</c>): right hand
/// type 0 to 12, and for each, left hand type 0 to 12 -- except for the types a byte table
/// marks as two-handed, 5, 6, 7 and 12, where the left hand is the right hand's type. That
/// is 9 x 13 + 4 = 121 combinations.
/// </para>
/// <para>
/// A set states ranges, not combinations: one inclusive range per variable, all of which
/// must hold. A group of combinations is therefore covered by rectangles, one set each.
/// The combinations nobody asks for -- a two-handed right hand with a different left --
/// may fall inside a rectangle either way, which keeps the rectangles few.
/// </para>
/// </remarks>
internal static class HandCombinations
{
    public const int Highest = 12;

    private static readonly bool[] TwoHanded =
        [false, false, false, false, false, true, true, true, false, false, false, false, true];

    /// <summary>Every (right, left) the game asks about, in the order it asks.</summary>
    public static IReadOnlyList<(int Right, int Left)> All { get; } = Enumerate();

    /// <summary>
    /// The combinations a character can hold: the game also asks about a two-handed
    /// weapon in the left hand alone (a crossbow, 12, beside a sword), which no one can
    /// equip and which the player's behaviour sends nowhere.
    /// </summary>
    public static IReadOnlyList<(int Right, int Left)> Holdable { get; } =
        [.. Enumerate().Where(h => !TwoHanded[h.Item2] || h.Item1 == h.Item2)];

    private static List<(int, int)> Enumerate()
    {
        var all = new List<(int, int)>();
        for (int right = 0; right <= Highest; right++)
        {
            if (TwoHanded[right]) { all.Add((right, right)); continue; }
            for (int left = 0; left <= Highest; left++) all.Add((right, left));
        }

        return all;
    }

    private static bool Asked((int Right, int Left) point) =>
        point.Right is >= 0 and <= Highest && point.Left is >= 0 and <= Highest
        && (!TwoHanded[point.Right] || point.Left == point.Right);

    /// <summary>
    /// Rectangles of (right, left) covering a group of combinations and no combination
    /// outside it.
    /// </summary>
    public static IReadOnlyList<(int RightMin, int RightMax, int LeftMin, int LeftMax)> Cover(
        IReadOnlyCollection<(int Right, int Left)> group)
    {
        var inGroup = new HashSet<(int, int)>(group);
        bool Allowed((int, int) p) => inGroup.Contains(p) || !Asked(p) && p.Item1 is >= 0 and <= Highest && p.Item2 is >= 0 and <= Highest;

        var covered = new HashSet<(int, int)>();
        var rectangles = new List<(int, int, int, int)>();

        foreach ((int right, int left) in group.OrderBy(p => p.Right).ThenBy(p => p.Left))
        {
            if (covered.Contains((right, left))) continue;

            int leftMax = left;
            while (leftMax < Highest && Allowed((right, leftMax + 1))) leftMax++;

            int rightMax = right;
            while (rightMax < Highest && Enumerable.Range(left, leftMax - left + 1).All(l => Allowed((rightMax + 1, l))))
                rightMax++;

            rectangles.Add((right, rightMax, left, leftMax));
            for (int r = right; r <= rightMax; r++)
                for (int l = left; l <= leftMax; l++)
                    covered.Add((r, l));
        }

        return rectangles;
    }
}
