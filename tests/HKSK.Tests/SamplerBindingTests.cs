using HKSK.Behavior;
using HKSK.Cache;
using HKSK.Model;
using HKX2;
using Xunit;

namespace HKSK.Tests;

/// <summary>
/// What the speed sampler is wired to, at all 44 locomotion roots.
/// </summary>
/// <remarks>
/// <c>BSSpeedSamplerModifier</c> has four fields and every one of them is a
/// binding rather than a stored value: it reads <c>state</c>, <c>direction</c> and
/// <c>goalSpeed</c> from graph variables and writes <c>speedOut</c> back to one.
/// The sampler is therefore only meaningful together with the variables it names,
/// and a variable index is only meaningful together with the file the sampler
/// lives in.
/// </remarks>
public sealed class SamplerBindingTests
{
    /// <summary>
    /// Every locomotion root's sampler carries a binding set, and every binding is
    /// to a graph variable.
    /// </summary>
    /// <remarks>
    /// 175 bindings over the 44 samplers, all of binding type 0 -- a graph
    /// variable rather than a character property. None is left unbound to a stored
    /// constant.
    /// </remarks>
    [CorpusFact]
    public void EverySamplerIsBoundToGraphVariables()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        int roots = 0, bindings = 0;

        foreach (ProjectLocation at in cache.LocateSpeedProjects())
            foreach (LocomotionRoot root in Locomotion.RootsOf(at.ProjectFile!))
            {
                roots++;

                hkbVariableBindingSet set = Assert.IsType<hkbVariableBindingSet>(
                    root.Sampler.m_variableBindingSet);

                Assert.NotEmpty(set.m_bindings);
                Assert.All(set.m_bindings, b => Assert.Equal(0, b.m_bindingType));

                bindings += set.m_bindings.Count;
            }

        Assert.Equal(44, roots);
        Assert.Equal(175, bindings);
    }

    /// <summary>
    /// Each field is bound to the same variable everywhere, resolved per file.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>direction</c> is always <c>Direction</c> and <c>goalSpeed</c> always
    /// <c>Speed</c>, in all 44. The output has three names -- <c>SpeedSampled</c>
    /// 31 times, <c>SampledSpeed</c> 9 and <c>HorseSpeedSampled</c> 4 -- which is
    /// why the variable a blend reads has to be matched against the sampler's own
    /// output rather than against a name anyone assumed.
    /// </para>
    /// <para>
    /// The names are resolved through <see cref="ProjectVariables"/>, against the
    /// file each sampler lives in. That is load-bearing: the dog's sampler is in
    /// <c>quadrupedbehavior.hkx</c>, not in <c>dogbehavior.hkx</c>, and its indices
    /// mean something else in the root file's table.
    /// </para>
    /// </remarks>
    [CorpusFact]
    public void EachFieldIsBoundToTheSameVariableEverywhere()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        var seen = new Dictionary<string, Dictionary<string, int>>();

        foreach (ProjectLocation at in cache.LocateSpeedProjects())
        {
            List<ProjectStep> walk = [.. ProjectVisitor.Visit(at.ProjectFile!)];
            var variables = new ProjectVariables(walk);

            foreach (LocomotionRoot root in Locomotion.RootsIn(walk))
                foreach (var binding in root.Sampler.m_variableBindingSet!.m_bindings)
                {
                    string name = Assert.IsType<string>(
                        variables.NameOf(root.File, binding.m_variableIndex));

                    if (!seen.TryGetValue(binding.m_memberPath, out var names))
                        seen[binding.m_memberPath] = names = [];

                    names.TryGetValue(name, out int count);
                    names[name] = count + 1;
                }
        }

        Assert.Equal(["direction", "goalSpeed", "speedOut", "state"], seen.Keys.Order());

        Assert.Equal(new Dictionary<string, int> { ["Direction"] = 44 }, seen["direction"]);
        Assert.Equal(new Dictionary<string, int> { ["Speed"] = 44 }, seen["goalSpeed"]);
        Assert.Equal(new Dictionary<string, int> { ["iState"] = 43 }, seen["state"]);

        Assert.Equal(
            new Dictionary<string, int>
            {
                ["SpeedSampled"] = 31,
                ["SampledSpeed"] = 9,
                ["HorseSpeedSampled"] = 4,
            },
            seen["speedOut"]);
    }

    /// <summary>
    /// 43 of the 44 samplers bind <c>state</c>, and the netch is the one that does
    /// not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>state</c> is the speed table's key: the game writes <c>iState</c> from
    /// the actor's movement type and the sampler reads it to choose which block of
    /// the table applies. Every sampler that binds it binds it to <c>iState</c>,
    /// with no second spelling anywhere.
    /// </para>
    /// <para>
    /// The netch binds only <c>direction</c>, <c>goalSpeed</c> and
    /// <c>speedOut</c>. Its stored <c>m_state</c> is -1, which is what every one of
    /// the 44 stores -- the field is a placeholder the binding overwrites at
    /// runtime -- so the netch's sampler runs with whatever -1 means and never
    /// selects a block. Its speed table has exactly one entry, key 0, which is
    /// consistent with a creature that never changes movement type.
    /// </para>
    /// </remarks>
    [CorpusFact]
    public void TheNetchIsTheOnlySamplerThatDoesNotBindItsState()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        var unbound = new List<string>();
        int bound = 0;

        foreach (ProjectLocation at in cache.LocateSpeedProjects())
            foreach (LocomotionRoot root in Locomotion.RootsOf(at.ProjectFile!))
            {
                // the stored value is a placeholder in every single one
                Assert.Equal(-1, root.Sampler.m_state);

                if (root.Sampler.m_variableBindingSet!.m_bindings.Any(b => b.m_memberPath == "state"))
                    bound++;
                else
                    unbound.Add(at.Name);
            }

        Assert.Equal(43, bound);
        Assert.Equal(["NetchProject"], unbound);

        // and its table has the single key that implies
        var keys = cache.SpeedData!.Block("NetchProject")!.Entries
            .Where(e => e.Records.Count > 0)
            .Select(e => e.Key)
            .ToList();

        Assert.Equal([0u], keys);
    }
}
