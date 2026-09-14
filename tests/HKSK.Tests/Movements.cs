namespace HKSK.Tests;

/// <summary>Movement types, for a test that has none to hand.</summary>
/// <remarks>
/// With no movement types a creature cannot be known to change gait, so the
/// rebuild answers every record with one ladder. That is the right behaviour for
/// a caller that does not supply them, and it is what the count in
/// <see cref="SpeedSamplerTests.TheTableRebuildsCurveByCurve"/> is measured
/// against -- the corpus figures do not depend on having the masters.
/// </remarks>
internal static class Movements
{
    public static readonly IReadOnlyDictionary<string, MovementType> None =
        new Dictionary<string, MovementType>();
}
