using System.Text;
using HKSK.Behavior;
using HKSK.Cache;
using HKSK.Engine;
using HKSK.Model;
using HKX2;
using Xunit;

namespace HKSK.Tests;

/// <summary>The expression language, against every expression the game ships.</summary>
public sealed class ExpressionTests
{
    [Theory]
    [InlineData("iState = iState_DeerDefault + iMovementSpeed", "iState", null)]
    [InlineData("turnSpeedMult = fabs(TurnDelta/90)", "turnSpeedMult", null)]
    [InlineData("weaponDraw if (iCombatStance == 1)", null, "weaponDraw")]
    [InlineData("BeginCastVoice if bWantCastVoice && bVoiceReady", null, "BeginCastVoice")]
    [InlineData("SoundPlay.WPNBowZoomIn if (iWantBlock)", null, "SoundPlay.WPNBowZoomIn")]
    public void TheTwoFormsAreRecognised(string text, string? target, string? sent)
    {
        Expression parsed = Assert.IsType<Expression>(Expression.Parse(text));

        Assert.Equal(target, parsed.Effect.Target);
        Assert.Equal(sent, parsed.Effect.Event);
    }

    /// <summary>Arithmetic, precedence and the seven functions.</summary>
    [Fact]
    public void ItEvaluates()
    {
        Variables variables = Variables.Of(("a", 3f), ("b", 4f), ("flag", 1f), ("out", 0f));

        Assert.Equal(11f, Value("out = a + b * 2", variables));
        Assert.Equal(14f, Value("out = (a + b) * 2", variables));
        Assert.Equal(1f, Value("out = a < b", variables));
        Assert.Equal(0f, Value("out = !flag", variables));
        Assert.Equal(3f, Value("out = fabs(0 - a)", variables));
        Assert.Equal(3.5f, Value("out = clamp(10, 1, 3.5)", variables));
        Assert.Equal(4f, Value("out = max(a, b)", variables));
        Assert.Equal(1f, Value("out = a % 2", variables));

        // cond is Bethesda's, and only the branch taken has to resolve.
        Assert.Equal(3f, Value("out = cond(flag, a, missingVariable)", variables));
        Assert.Equal(4f, Value("out = cond(!flag, missingVariable, b)", variables));
    }

    /// <summary>A name the graph does not declare makes the expression unevaluable.</summary>
    [Fact]
    public void AnUndeclaredNameIsNotZero()
    {
        Variables variables = Variables.Of(("out", 0f));
        Expression parsed = Assert.IsType<Expression>(Expression.Parse("out = nothingNamedThis + 1"));

        Assert.False(parsed.TryEvaluate(variables, out _));
    }

    private static float Value(string text, Variables variables)
    {
        Expression parsed = Assert.IsType<Expression>(Expression.Parse(text));
        Assert.True(parsed.TryEvaluate(variables, out float value), text);

        return value;
    }

    /// <summary>Every expression the 49 projects contain parses.</summary>
    [CorpusFact]
    public void TheWholeCorpusParses()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        HashSet<string> all = [];
        List<string> refused = [];

        foreach (ProjectLocation at in cache.LocateSpeedProjects().Where(p => p.Found))
            foreach (ProjectStep step in ProjectWalk.Of(at.ProjectFile!).Steps)
                if (step.Node is hkbExpressionData data && data.m_expression is { Length: > 0 })
                    all.Add(data.m_expression);

        foreach (string text in all)
            if (Expression.Parse(text) is null) refused.Add(text);

        Assert.Equal(261, all.Count);
        Assert.True(refused.Count == 0,
            $"{refused.Count} did not parse:\n{string.Join("\n", refused.Take(10))}");
    }
}
