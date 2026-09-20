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
    [InlineData("iRightHandType > 0", null, null)]
    [InlineData("(iLeftHandType != 5) && (iLeftHandType != 0)", null, null)]
    [InlineData("bWantCastLeft == 0", null, null)]
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

        // A bare condition, as a transition carries it: its value, nothing written.
        Assert.Equal(1f, Value("a > 2 && b == 4", variables));
        Assert.Equal(0f, Value("(a != 3) || !flag", variables));
        Assert.Equal(3f, variables.Real(variables.IndexOf("a")));

        // cond is Bethesda's, and only the branch taken has to resolve.
        Assert.Equal(3f, Value("out = cond(flag, a, missingVariable)", variables));
        Assert.Equal(4f, Value("out = cond(!flag, missingVariable, b)", variables));
    }

    /// <summary>
    /// The answers Havok itself gives, for the part of the language 6.6 compiles.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These are not chosen cases: they are what the 6.6 runtime does, read out of
    /// it by <c>tools/hkmeasure states</c>, which drives a state machine from an
    /// <c>hkbEvaluateExpressionModifier</c> and sweeps a variable. Setting
    /// <c>EXPR="sel = Speed &gt; 100|iState = iState_Base + sel"</c> and sweeping
    /// <c>Speed</c> from 0 to 200 gives <c>sel</c> 0 at 0, 40 and 80 and 1 at 120,
    /// 160 and 200, with <c>iState</c> following at 20 and 21 against an
    /// <c>iState_Base</c> of 20.
    /// </para>
    /// <para>
    /// So a comparison yields 1 or 0 and is a value like any other, and the
    /// parentheses are optional -- <c>sel = (Speed &gt; 100)</c> compiles to the
    /// same six tokens and answers identically.
    /// </para>
    /// <para>
    /// <strong><c>cond</c> compiles to nothing</strong>, which the same harness
    /// shows directly rather than by inference: <c>sel = cond((Speed &lt; 100), 0,
    /// 1)</c> alongside <c>iState = iState_Base + sel</c> compiles to three tokens
    /// in total, and reading the compiled RPN back gives only
    /// <c>iState_Base</c>, <c>sel</c>, <c>OP_ADD</c>. The first expression
    /// contributes none, never assigns <c>sel</c>, and the sweep reads 20 at every
    /// speed. The engine implements it anyway because Bethesda's own runtime does.
    /// </para>
    /// </remarks>
    [Fact]
    public void ItAgreesWithTheRuntimeOnWhatSixSixCompiles()
    {
        Variables variables = Variables.Of(("Speed", 0f), ("sel", 0f), ("iState", 0f), ("iState_Base", 20f));

        foreach ((float speed, float expected) in new[]
        {
            (0f, 0f), (40f, 0f), (80f, 0f), (120f, 1f), (160f, 1f), (200f, 1f),
        })
        {
            variables.Set("Speed", speed);

            Assert.Equal(expected, Value("sel = Speed > 100", variables));
            Assert.Equal(expected, Value("sel = (Speed > 100)", variables));

            variables.Set("sel", expected);
            Assert.Equal(20f + expected, Value("iState = iState_Base + sel", variables));
        }
    }

    /// <summary>
    /// Every operator and function 6.6 compiles, against the answers it gives.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each row is a sweep of <c>Speed</c> through 0, 40, 80, 120, 160 and 200 with
    /// <c>tools/hkmeasure states</c>, read out of an <c>hkbEvaluateExpressionModifier</c>
    /// the 6.6 runtime compiled and ran. The result goes into an <c>INT32</c>
    /// variable, so these are Havok's answers already truncated, and the engine is
    /// compared the same way.
    /// </para>
    /// <para>
    /// <strong>The two trigonometric functions do not agree with each other, and
    /// that is Havok.</strong> <c>sind(40)</c> reads 64 and <c>sind(200)</c> reads
    /// -34, which is sine in <em>degrees</em>; <c>cos(40)</c> reads -66 and
    /// <c>cos(120)</c> reads 81, which is cosine in <em>radians</em>. The engine
    /// had them that way round already, and now it is measured rather than read off
    /// the name.
    /// </para>
    /// <para>
    /// <c>clamp</c> takes the value first and then its bounds; <c>!</c> is a logical
    /// negation, 1 only where its operand is zero; and division binds tighter than
    /// addition.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("out = Speed % 100", new[] { 0, 40, 80, 20, 60, 0 })]
    [InlineData("out = max(Speed, 100)", new[] { 100, 100, 100, 120, 160, 200 })]
    [InlineData("out = min(Speed, 100)", new[] { 0, 40, 80, 100, 100, 100 })]
    [InlineData("out = clamp(Speed, 50, 150)", new[] { 50, 50, 80, 120, 150, 150 })]
    [InlineData("out = fabs(0 - Speed)", new[] { 0, 40, 80, 120, 160, 200 })]
    [InlineData("out = !Speed", new[] { 1, 0, 0, 0, 0, 0 })]
    [InlineData("out = Speed / 2 + 1", new[] { 1, 21, 41, 61, 81, 101 })]
    [InlineData("out = sind(Speed) * 100", new[] { 0, 64, 98, 86, 34, -34 })]
    [InlineData("out = cos(Speed) * 100", new[] { 100, -66, -11, 81, -97, 48 })]
    [InlineData("out = Speed > 100", new[] { 0, 0, 0, 1, 1, 1 })]
    public void ItGivesTheAnswersTheRuntimeGives(string text, int[] expected)
    {
        Variables variables = Variables.Of(("Speed", 0f), ("out", 0f));
        float[] speeds = [0f, 40f, 80f, 120f, 160f, 200f];

        for (int i = 0; i < speeds.Length; i++)
        {
            variables.Set("Speed", speeds[i]);
            Assert.Equal(expected[i], (int)Value(text, variables));
        }
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
