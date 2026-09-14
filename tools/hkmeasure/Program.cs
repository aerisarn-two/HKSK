using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Havok;
using HavokManaged;

// hkmeasure -- drive a synchronised hkbBlenderGenerator over its blend
// parameter and report the root-motion speed it actually delivers, so the
// closed form in speed-data.md section 6 can be checked against the runtime.
static class Probe
{
    // Every wrapper here owns a native object and frees it on finalize, so a
    // local that the JIT considers dead can tear Havok down mid-run.  Root them.
    static readonly List<object> Keep = new List<object>();
    static T Root<T>(T o) { Keep.Add(o); return o; }

    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);

    const int FPS = 30;

    // Phase 1.  Emit a skeleton and two clips as an XML packfile, written by
    // Havok's own serialiser so the schema is right by construction.  The root
    // motion is added afterwards by patch_rf.py: hkaDefaultAnimatedReferenceFrame
    // is the one class here with no managed constructor.
    static void Emit(string outPath, float durationA, float durationB)
    {
        var skel = Root(new hkaSkeleton());
        skel.m_name = "rig";
        var bone = Root(new hkaBone());
        bone.m_name = "NPC Root [Root]";
        bone.m_lockTranslation = false;
        skel.m_bones.Set(new hkaBone[] { bone });
        skel.m_parentIndices.Set(new short[] { -1 });
        skel.m_referencePose.Set(new hkQsTransform[] { Identity });

        var anims = new hkaAnimation[2];
        var binds = new hkaAnimationBinding[2];
        float[] durs = { durationA, durationB };
        for (int i = 0; i < 2; i++)
        {
            int frames = (int)Math.Round(durs[i] * FPS) + 1;
            var a = Root(new hkaInterleavedUncompressedAnimation());
            a.m_duration = durs[i];
            a.m_numberOfTransformTracks = 1;
            a.m_numberOfFloatTracks = 0;
            var pose = new hkQsTransform[frames];
            for (int f = 0; f < frames; f++) pose[f] = Identity;
            a.m_transforms.Set(pose);

            var b = Root(new hkaAnimationBinding());
            b.m_animation = a;
            b.m_originalSkeletonName = "rig";
            b.m_transformTrackToBoneIndices.Set(new short[] { 0 });
            anims[i] = a; binds[i] = b;
        }

        var ac = Root(new hkaAnimationContainer());
        ac.m_skeletons.Set(new hkaSkeleton[] { skel });
        ac.m_animations.Set(anims);
        ac.m_bindings.Set(binds);

        bool ok = HavokPackfile.saveInRootLevelContainer(
            outPath, ac, HavokPackfile.FileFormat.FORMAT_XML_PACKFILE);
        Console.WriteLine($"[emit] {outPath} -> {ok}  (durations {durationA}, {durationB})");
    }

    static hkQsTransform Identity =>
        new hkQsTransform(new hkVector4(0, 0, 0, 0),
                          new hkQuaternion(0, 0, 0, 1),
                          new hkVector4(1, 1, 1, 1));


    static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "emit")
        {
            HavokSystem.Init();
            Root(new hbtHavokEnvironment());
            Emit(args.Length > 1 ? args[1] : "rig.xml",
                 args.Length > 2 ? float.Parse(args[2], CultureInfo.InvariantCulture) : 1.0f,
                 args.Length > 3 ? float.Parse(args[3], CultureInfo.InvariantCulture) : 0.8f);
            GC.KeepAlive(Keep);
            HavokSystem.Terminate();
            return;
        }

        string path = args.Length > 0 ? args[0] : "rigrf.xml";
        int steps = args.Length > 1 ? int.Parse(args[1]) : 20;

        HavokSystem.Init();
        var env = Root(new hbtHavokEnvironment());

        object root = Root(HavokPackfile.load(path));
        if (root == null) { Console.WriteLine("[load] FAILED (null)"); return; }
        Console.WriteLine("[load] " + root.GetType().FullName);

        hkaAnimationContainer ac = null;
        if (root is hkRootLevelContainer rlc)
            for (int i = 0; i < rlc.m_namedVariants.Count; i++)
            {
                var nv = rlc.m_namedVariants[i];
                Console.WriteLine($"[load]  [{i}] {nv.m_name} : {nv.m_className}");
                if (nv.m_variant is hkaAnimationContainer c) ac = c;
            }
        else ac = root as hkaAnimationContainer;
        if (ac == null) { Console.WriteLine("[load] no hkaAnimationContainer"); return; }

        var skel = ac.m_skeletons[0];
        Console.WriteLine($"[rig] skeleton '{skel.m_name}' bones={skel.m_bones.Count}");

        // Report what each clip actually carries, so a bad patch shows up here
        // rather than as a puzzling blend result.
        var clipSpeed = new float[ac.m_bindings.Count];
        var durs = new double[ac.m_bindings.Count];
        for (int i = 0; i < ac.m_bindings.Count; i++)
        {
            var an = ac.m_bindings[i].m_animation;
            var rf = an.m_extractedMotion as hkaDefaultAnimatedReferenceFrame;
            if (rf == null) { Console.WriteLine($"[clip] {i}: NO EXTRACTED MOTION"); continue; }
            var samples = rf.m_referenceFrameSamples;
            float travel = samples[samples.Count - 1].x;
            clipSpeed[i] = travel / an.m_duration;
            durs[i] = an.m_duration;
            Console.WriteLine($"[clip] {i}: duration={F(an.m_duration)} frames={samples.Count}"
                            + $" travel={F(travel)} speed={F(clipSpeed[i])}");
        }

        var ch = Root(Methods.createCharacter());
        Methods.setSkeleton(ch, (IntPtr)skel);

        var blender = Root(new hkbBlenderGenerator());
        blender.m_name = "ladder";
        // FLAG_SYNC=1, FLAG_SMOOTH_GENERATOR_WEIGHTS=4, FLAG_DONT_DEACTIVATE..=8,
        // FLAG_PARAMETRIC_BLEND=16, FLAG_IS_PARAMETRIC_BLEND_CYCLIC=32, FLAG_FORCE_DENSE_POSE=64.
        // A speed ladder is parametric but not cyclic: 0x11.  The direction
        // compass is 0x31, where the parameter wraps.
        string fenv = Environment.GetEnvironmentVariable("FLAGS");
        blender.m_flags = (short)(fenv == null ? 0x11 : Convert.ToInt16(fenv, 16));
        Console.WriteLine($"[blend] flags=0x{blender.m_flags:x}");
        blender.m_indexOfSyncMasterChild = -1;
        blender.m_minCyclicBlendParameter = 0f;
        blender.m_maxCyclicBlendParameter = 1f;
        blender.m_subtractLastChild = false;

        var clips = new List<hkbClipGenerator>();
        for (int i = 0; i < ac.m_bindings.Count; i++)
        {
            var clip = Root(new hkbClipGenerator());
            clip.m_name = "clip" + i;
            clip.m_animationName = "clip" + i;
            clip.m_playbackSpeed = 1f;
            clip.m_animationBindingIndex = -1;
            clip.m_mode = hkbClipGenerator.PlaybackMode.MODE_LOOPING;
            Methods.setAnimationBinding(clip, (IntPtr)ac.m_bindings[i]);

            var kid = Root(new hkbBlenderGeneratorChild());
            kid.m_generator = clip;
            kid.m_weight = i;               // rungs at 0 and 1
            kid.m_worldFromModelWeight = 1f;
            blender.m_children.Add(kid);
            clips.Add(clip);
        }

        var graph = Root(Methods.createBehavior());
        int soloClip = Environment.GetEnvironmentVariable("SOLO") == null
                     ? -1 : int.Parse(Environment.GetEnvironmentVariable("SOLO"));
        if (soloClip >= 0) { Methods.setRootGenerator(graph, clips[soloClip]); Console.WriteLine("[graph] root = clip " + soloClip); }
        else Methods.setRootGenerator(graph, blender);
        var ctx = Root(new hkbContext());
        Methods.setCharacter(ctx, ch);
        Methods.setUpVector(ctx, new hkVector4(0, 0, 1, 0));
        Methods.activate(graph, ctx);
        Console.WriteLine("[graph] activated");
        for (int i = 0; i < clips.Count; i++)
        {
            Methods.setAnimationBinding(clips[i], (IntPtr)ac.m_bindings[i]);   // activate may re-resolve
            Console.WriteLine($"[clip] {i}: durationLocalTime={F(Methods.getDurationLocalTime(clips[i]))}");
        }

        // Methods.generate(graph, context, isAdditive, numBones, timestep) is the
        // per-character step; it returns the frame's root motion directly and
        // skips the list plumbing of the public overload.  It is not public.
        var step = typeof(Methods).GetMethod("generate",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new[] { typeof(hkbBehaviorGraph), typeof(hkbContext), typeof(bool), typeof(int), typeof(float) },
            null);
        if (step == null) { Console.WriteLine("[step] inner generate not found"); return; }

        const float dt = 1f / 60f;
        const int settle = 120;
        int sample = Environment.GetEnvironmentVariable("SAMPLE") == null
                   ? 600 : int.Parse(Environment.GetEnvironmentVariable("SAMPLE"));
        var arg = new object[] { graph, ctx, false, 1, dt };
        int probeN = 6;
        var setPose = typeof(Methods).GetMethod("setPoseFromOutputToCharacter",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            null, new[] { typeof(hkbGeneratorOutput), typeof(hkbCharacter) }, null);

        // The public overload reads worldFromModel off the output and writes it
        // back onto the character; that write-back is what accumulates motion.
        // worldFromModel accumulates, and at a few hundred units a second a
        // float32 stops resolving a 1.7-unit increment within a minute.  Reset
        // the character to the origin every frame so the value read back is the
        // frame's own delta, and accumulate in double instead.
        var identity = new hkQsTransform(new hkVector4(0, 0, 0, 0),
                                         new hkQuaternion(0, 0, 0, 1),
                                         new hkVector4(1, 1, 1, 1));
        double accX = 0, accY = 0;
        Action Step = () =>
        {
            Methods.setWorldFromModel(ch, identity);
            var outp = (hkbGeneratorOutput)step.Invoke(null, arg);
            hkQsTransform w = Methods.getWorldFromModel(outp);
            accX += w.m_translation.x; accY += w.m_translation.y;
            if (probeN > 0)
            {
                setPose.Invoke(null, new object[] { outp, ch });
                hkQsTransform bt = Methods.getBoneTransform(ch, 0);
                Console.WriteLine($"[out] bones={Methods.getNumBones(outp)}"
                    + $" wfm=({F(w.m_translation.x)},{F(w.m_translation.y)},{F(w.m_translation.z)})"
                    + $" bone0.z={F(bt.m_translation.z)}");
                probeN--;
            }
            Methods.setWorldFromModel(ch, w);
        };
        Func<hkQsTransform> Where = () => Methods.getWorldFromModel(ch);

        Console.WriteLine("x,delivered,predicted,error,u_effective,x-u,chord");
        for (int s = 0; s <= steps; s++)
        {
            float x = (float)s / steps;

            // activate() clones the node tree, so the blend parameter has to be
            // set on the template before the clone is taken.
            blender.m_blendParameter = x;
            Methods.deactivate(graph, ctx);
            Methods.activate(graph, ctx);
            for (int i = 0; i < clips.Count; i++)
                Methods.setAnimationBinding(clips[i], (IntPtr)ac.m_bindings[i]);

            for (int i = 0; i < settle; i++) Step();
            accX = 0; accY = 0;
            for (int i = 0; i < sample; i++) Step();

            double delivered = Math.Sqrt(accX * accX + accY * accY) / (sample * dt);
            float chord = clipSpeed[0] + (clipSpeed[1] - clipSpeed[0]) * x;

            // Section 6: y = |lerp(travel_a, travel_b, u)| / lerp(d_a, d_b, u).
            // Invert it for the u the runtime actually used, so the residual
            // between that and the nominal x is visible.
            double ta = clipSpeed[0] * durs[0], tb = clipSpeed[1] * durs[1];
            double pred = (ta + (tb - ta) * x) / (durs[0] + (durs[1] - durs[0]) * x);
            double uEff = (delivered * durs[0] - ta) / ((tb - ta) - delivered * (durs[1] - durs[0]));
            Console.WriteLine($"{F(x)},{delivered:F6},{pred:F6},{(delivered - pred):+0.000000;-0.000000}," 
                            + $"{uEff:F8},{(x - uEff):+0.00000000;-0.00000000},{F(chord)}");
            Console.Out.Flush();
        }

        GC.KeepAlive(Keep);
        HavokSystem.Terminate();
    }
}
