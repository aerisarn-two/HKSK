using HKSK.SetData;
using Xunit;

namespace HKSK.Tests;

/// <summary>
/// The weapon combinations a race asks the set data about, and how a group of them is
/// written as the ranges a set can state.
/// </summary>
public sealed class HandCombinationTests
{
    [Fact]
    public void TheRaceAsksAbout121Combinations()
    {
        // right 0-12; a two-handed right (5, 6, 7, 12) is asked with itself in the left
        // hand only, every other right with every left: 9 x 13 + 4
        Assert.Equal(121, HandCombinations.All.Count);
        Assert.Equal(121, HandCombinations.All.Distinct().Count());
        Assert.Contains((5, 5), HandCombinations.All);
        Assert.DoesNotContain((5, 0), HandCombinations.All);
        Assert.Contains((1, 12), HandCombinations.All);
    }

    [Fact]
    public void NoOneHoldsATwoHandedWeaponInTheLeftHandAlone()
    {
        // the nine one-handed rights lose their four two-handed lefts
        Assert.Equal(121 - 9 * 4, HandCombinations.Holdable.Count);
        Assert.DoesNotContain((1, 12), HandCombinations.Holdable);
        Assert.Contains((12, 12), HandCombinations.Holdable);
    }

    [Fact]
    public void AContiguousGroupIsOneRange()
    {
        var group = new List<(int, int)>();
        for (int right = 1; right <= 3; right++)
            for (int left = 0; left <= 4; left++)
                group.Add((right, left));

        Assert.Equal([(1, 3, 0, 4)], HandCombinations.Cover(group));
    }

    [Fact]
    public void ARangeMaySpanCombinationsNobodyAsksAbout()
    {
        // a two-handed right is only asked about with itself in the left hand, so the rows
        // for 5 and 6 are empty but for (5, 5) and (6, 6): one range covers both, and runs
        // on over lefts nobody pairs with them
        Assert.Equal([(5, 6, 5, 12)], HandCombinations.Cover([(5, 5), (6, 6)]));
    }

    [Fact]
    public void TheRangesCoverTheGroupAndNothingOutsideIt()
    {
        List<(int, int)> group = [(0, 0), (0, 1), (1, 1), (3, 9), (8, 8), (9, 8)];
        var ranges = HandCombinations.Cover(group);

        var covered = HandCombinations.All
            .Where(h => ranges.Any(r => h.Right >= r.RightMin && h.Right <= r.RightMax && h.Left >= r.LeftMin && h.Left <= r.LeftMax))
            .ToHashSet();

        Assert.Equal(group.ToHashSet(), covered);
    }
}
