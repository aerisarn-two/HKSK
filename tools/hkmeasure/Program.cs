using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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


    // Phase 3.  Watch a behaviour decide.  The question the static analysis
    // could not answer is which locomotion state serves which iState key, and
    // for most creatures the graph answers it with an expression:
    //
    //     iMovementSpeed = cond((Speed < 100), 0, 1)
    //     iState         = iState_DeerDefault + iMovementSpeed
    //
    // with the state machine binding startStateId to that same iMovementSpeed.
    // This builds exactly that shape and sweeps Speed, printing iState beside
    // the state actually entered, so the pairing is observed rather than
    // inferred.  The deer is the creature it reproduces.
    static void States(string path, float from, float to, int steps)
    {
        var env = Root(new hbtHavokEnvironment());

        object root = Root(HavokPackfile.load(path));
        var rlc = root as hkRootLevelContainer;
        hkaAnimationContainer ac = null;
        if (rlc != null)
            for (int i = 0; i < rlc.m_namedVariants.Count; i++)
                if (rlc.m_namedVariants[i].m_variant is hkaAnimationContainer c) ac = c;
        else ac = root as hkaAnimationContainer;
        if (ac == null) { Console.WriteLine("[load] no hkaAnimationContainer"); return; }

        var ch = Root(Methods.createCharacter());
        Methods.setSkeleton(ch, (IntPtr)ac.m_skeletons[0]);

        // one clip per state, so the state that ran is visible in the motion too
        var clips = new List<hkbClipGenerator>();
        for (int i = 0; i < ac.m_bindings.Count && i < 2; i++)
        {
            var clip = Root(new hkbClipGenerator());
            clip.m_name = "clip" + i;
            clip.m_animationName = "clip" + i;
            clip.m_playbackSpeed = 1f;
            clip.m_animationBindingIndex = -1;
            clip.m_mode = hkbClipGenerator.PlaybackMode.MODE_LOOPING;
            Methods.setAnimationBinding(clip, (IntPtr)ac.m_bindings[i]);
            clips.Add(clip);
        }

        // the machine: state 0 walks, state 1 runs, chosen by startStateId
        var machine = Root(new hkbStateMachine());
        machine.m_name = "Locomotion";
        machine.m_startStateId = 0;
        machine.m_syncVariableIndex = -1;
        machine.m_maxSimultaneousTransitions = 32;
        for (int i = 0; i < clips.Count; i++)
        {
            var info = Root(new hkbStateMachine.StateInfo());
            info.m_name = i == 0 ? "WalkState" : "RunState";
            info.m_stateId = i;
            info.m_probability = 1f;
            info.m_enable = true;
            info.m_generator = clips[i];
            machine.m_states.Add(info);
        }

        var expr = Root(new hkbEvaluateExpressionModifier());
        expr.m_name = "Locomotion_EEM";
        expr.m_enable = true;

        var data = Root(new hkbExpressionDataArray());
        string exprEnv = Environment.GetEnvironmentVariable("EXPR");
        string[] texts = exprEnv != null
            ? exprEnv.Split('|')
            : new[] { "sel = cond((Speed < 100), 0, 1)", "iState = iState_Base + sel" };
        foreach (string text in texts)
        {
            var e = Root(new hkbExpressionData());
            e.m_expression = text;
            e.m_assignmentVariableIndex = -1;
            e.m_assignmentEventIndex = -1;
            data.m_expressionsData.Add(e);
        }
        expr.m_expressions = data;

        var graph = Root(Methods.createBehavior());
        var modGen = Root(Methods.createModifierGenerator(expr, machine));
        Methods.setRootGenerator(graph, modGen);

        // a graph that discards its variables when inactive loses whatever the
        // modifier wrote between the deactivate and the next read
        graph.m_variableMode = hkbBehaviorGraph.VariableMode.VARIABLE_MODE_MAINTAIN_VALUES_WHEN_INACTIVE;
        Console.WriteLine($"[graph] variableMode={graph.m_variableMode} " +
                          $"modifier={(modGen.m_modifier == null ? "null" : modGen.m_modifier.m_name)} " +
                          $"generator={(modGen.m_generator == null ? "null" : "set")}");

        int vPb = Methods.addVariableReal(graph, "pb");
        int vSpeed = Methods.addVariableReal(graph, "Speed");
        int vSel = Methods.addVariableInt32(graph, "sel");
        int vState = Methods.addVariableInt32(graph, "iState");
        int vBase = Methods.addVariableInt32(graph, "iState_Base");
        Console.WriteLine($"[vars] pb={vPb} Speed={vSpeed} sel={vSel} iState={vState} iState_Base={vBase}");
        var gd = graph.m_data;
        Console.WriteLine($"[vars] data={(gd == null ? "NULL" : "ok")} " +
                          $"infos={(gd?.m_variableInfos == null ? -1 : gd.m_variableInfos.Count)} " +
                          $"names={(gd?.m_stringData?.m_variableNames == null ? -1 : gd.m_stringData.m_variableNames.Count)} " +
                          $"initial={(gd?.m_variableInitialValues?.m_wordVariableValues == null ? -1 : gd.m_variableInitialValues.m_wordVariableValues.Count)}");

        // startStateId <- sel, the binding that makes the key and the state one number
        // observable channel: if the modifier really runs, a clip whose playback
        // speed is bound to a variable it writes will report a different duration.
        var clipBind = Root(new hkbVariableBindingSet());
        var pbBind = Root(new hkbVariableBindingSet.Binding());
        pbBind.m_memberPath = "playbackSpeed";
        pbBind.m_variableIndex = vPb;
        pbBind.m_bindingType = 0;
        clipBind.m_bindings.Add(pbBind);
        clips[0].m_variableBindingSet = clipBind;

        var bind = Root(new hkbVariableBindingSet());
        var one = Root(new hkbVariableBindingSet.Binding());
        one.m_memberPath = "startStateId";
        one.m_variableIndex = vSel;
        one.m_bindingType = 0;
        bind.m_bindings.Add(one);
        machine.m_variableBindingSet = bind;

        var ctx = Root(new hkbContext());
        // A context built by hand has no project data, and the public generate
        // path walks into it -- that is where setCharacter was faulting.
        ctx.m_projectData = Root(Methods.createProjectData());
        Methods.setCharacter(ctx, ch);
        Methods.setUpVector(ctx, new hkVector4(0, 0, 1, 0));
        Console.WriteLine($"[ctx] projectData={(ctx.m_projectData == null ? "null" : "ok")} " +
                          $"characterSetup={(Methods.getCharacterSetup(ch) == null ? "NULL" : "ok")} " +
                          $"numBones={Methods.getNumBones(ch)}");

        // The Tool registers each character with the environment before stepping.
        // addCharacter takes the unmanaged hkbCharacter, which `using Havok` hides
        // behind the managed wrapper of the same name -- hence global::.
        // The public overload is the one the Tool drives, and it is the one that
        // runs the modifier pass -- the inner per-character generate the blend
        // sweep uses skips it, which is why an expression compiled but never ran.
        var chars = new List<hkbCharacter> { ch };
        var graphs = new List<hkbBehaviorGraph> { graph };
        var ctxs = new List<hkbContext> { ctx };
        var qIn = new List<List<hkbEvent>> { new List<hkbEvent>() };
        var qOut = new List<List<hkbEvent>> { new List<hkbEvent>() };
        var flagsA = new List<bool> { false };   // stateChanged
        var flagsB = new List<bool> { false };   // variablesChanged
        var bones = new List<int> { 1 };

        // generate(characters, behaviors, allCharactersEventQueue, contexts,
        //          generatorOutputListener, stateChanged, variablesChanged,
        //          numBones, worldUp, timestep, step, isfirstCharacterAdditive,
        //          eventQueueOut) -- worldUp is a value type and must not be null.
        var worldUp = new hkVector4(0, 0, 1, 0);

        // The listener is what the public overload hangs its generator outputs
        // off; passed null it has nowhere to put them.
        var listener = Root(new hkbPoseStoringGeneratorOutputListener());

        // The public Methods.generate calls the non-public generateUpToSceneModifiers
        // and passes nothing for the one argument it adds -- a list of generator
        // outputs -- which is what the NullReferenceException deeper in setCharacter
        // was really about. Call the inner one with the list supplied. It has to run
        // after activate(), since getAllVariableValues reads what activation builds.
        var inner = typeof(Methods).GetMethod("generateUpToSceneModifiers",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (inner == null) { Console.WriteLine("[step] generateUpToSceneModifiers not found"); return; }

        var outs = new List<hkbGeneratorOutput>();
        var args = new object[]
        {
            chars, graphs, qIn, listener, flagsA, flagsB, bones, worldUp,
            outs, ctxs, 1f / FPS, true, false, qOut,
        };

        Action Step = () => inner.Invoke(null, args);

        Console.WriteLine();
        Console.WriteLine("     Speed     sel   iState   stateId  stateName");

        for (int s = 0; s <= steps; s++)
        {
            float speed = steps == 0 ? from : from + (to - from) * s / steps;

            // Two passes.  The expression computes sel during generate, but the
            // machine reads startStateId when it activates -- so the first pass
            // works out sel and the second lets the machine act on it.  That
            // ordering is itself a finding: within one frame the state lags the
            // key by exactly one activation.
            int sel = 0, state = 0, id = -1;
            for (int pass = 0; pass < 2; pass++)
            {
                // set on the template first: activate() clones, and a value written
                // after the clone may not reach the instance that runs.
                Methods.setVariableValueInt32(graph, vBase, 20);      // iState_DeerDefault
                Methods.setVariableValueReal(graph, vSpeed, speed);
                if (pass > 0) Methods.setVariableValueInt32(graph, vSel, sel);

                Methods.activate(graph, ctx);
                for (int i = 0; i < clips.Count; i++)
                    Methods.setAnimationBinding(clips[i], (IntPtr)ac.m_bindings[i]);

                Methods.setVariableValueInt32(graph, vBase, 20);
                Methods.setVariableValueReal(graph, vSpeed, speed);

                for (int f = 0; f < 4; f++) Step();

                sel = Methods.getVariableValueInt32(graph, vSel);
                state = Methods.getVariableValueInt32(graph, vState);
                id = Methods.getCurrentStateId(machine);

                if (s == 0)
                {
                    var cs = expr.m_compiledExpressionSet;
                    Console.WriteLine($"[pass {pass}] Base={Methods.getVariableValueInt32(graph, vBase)} " +
                                      $"Speed={F(Methods.getVariableValueReal(graph, vSpeed))} " +
                                      $"expr={(expr.m_expressions == null ? "null" : expr.m_expressions.m_expressionsData.Count.ToString())} " +
                                      $"compiled={(cs == null ? "NULL" : cs.m_rpn.Count + " token, " + cs.m_numExpressions + " espressioni")}");
                    if (cs != null)
                    {
                        Console.WriteLine($"        starts=[{string.Join(",", System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(System.Linq.Enumerable.Range(0, cs.m_expressionToRpnIndex.Count), k => cs.m_expressionToRpnIndex[k].ToString())))}]");
                        for (int t = 0; t < cs.m_rpn.Count && t < 16; t++)
                            Console.WriteLine($"        rpn[{t}] op={cs.m_rpn[t].m_operator} data={F(cs.m_rpn[t].m_data)} type={cs.m_rpn[t].m_type}");
                    }
                    Console.WriteLine($"        clip0 durationLocalTime={F(Methods.getDurationLocalTime(clips[0]))} " +
                                      $"pb(read)={F(Methods.getVariableValueReal(graph, vPb))}");
                    for (int k = 0; k < data.m_expressionsData.Count; k++)
                        Console.WriteLine($"        expr[{k}] assignVar={data.m_expressionsData[k].m_assignmentVariableIndex}" +
                                          $" assignEvt={data.m_expressionsData[k].m_assignmentEventIndex}" +
                                          $"  \"{data.m_expressionsData[k].m_expression}\"");
                }

                if (pass == 0) Methods.deactivate(graph, ctx);
            }

            Console.WriteLine($"  {F(speed),8}  {sel,6}  {state,7}  {id,8}  {Methods.getStateName(machine)}");
            Methods.deactivate(graph, ctx);
        }
    }

    // Discovery, not production: the assembly only loads under its own runtime,
    // so the one way to see what it offers is to ask it there.
    /// Looks for the way a managed wrapper carries its native pointer, and for
    /// whatever the environment needs before the public generate path will run.
    static void Poke()
    {
        var env = Root(new hbtHavokEnvironment());
        Console.WriteLine($"[env] isInitialised={hbtHavokEnvironment.isInitialised()}");

        var envT = typeof(hbtHavokEnvironment);
        foreach (var m in new[] { "getWorld", "getEventLinker", "getAttachmentManager" })
        {
            var mi = envT.GetMethod(m, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            object got = null;
            try { got = mi?.Invoke(env, null); } catch (Exception e) { got = "threw " + e.InnerException?.GetType().Name; }
            Console.WriteLine($"[env] {m} -> {Describe(got)}");
        }

        var ch = Root(Methods.createCharacter());
        Console.WriteLine($"[char] {ch.GetType().FullName}");
        foreach (var f in ch.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic |
                                                 BindingFlags.Instance | BindingFlags.Static))
            Console.WriteLine($"    field {f.FieldType.Name} {f.Name} = {Describe(SafeGet(f, ch))}");
        foreach (var pr in ch.GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic |
                                                      BindingFlags.Instance))
            Console.WriteLine($"    prop  {pr.PropertyType.Name} {pr.Name}");
        for (System.Type t = ch.GetType(); t != null && t != typeof(object); t = t.BaseType)
        {
            Console.WriteLine($"    --- {t.FullName}");
            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic |
                                          BindingFlags.Instance | BindingFlags.Static |
                                          BindingFlags.DeclaredOnly))
                Console.WriteLine($"        field {f.FieldType.Name} {f.Name} = {Describe(SafeGet(f, ch))}");
            foreach (var pr in t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic |
                                               BindingFlags.Instance | BindingFlags.DeclaredOnly))
                Console.WriteLine($"        prop {pr.PropertyType.Name} {pr.Name}");
            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                                           BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                if (m.Name.IndexOf("ptr", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    m.Name.IndexOf("native", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    m.Name.StartsWith("op_"))
                    Console.WriteLine($"        method {m.ReturnType.Name} {m.Name}({m.GetParameters().Length})");
        }

        var add = envT.GetMethod("addCharacter", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        Console.WriteLine($"[add] {add?.ToString() ?? "not found"}  param={add?.GetParameters()[0].ParameterType}");
    }

    static object SafeGet(FieldInfo f, object o)
    {
        try { return f.GetValue(o); } catch (Exception e) { return "threw " + e.GetType().Name; }
    }

    static string Describe(object o) =>
        o == null ? "null" : $"{o.GetType().Name}:{o}";

    static void Api(string filter)
    {
        var asm = typeof(Methods).Assembly;
        foreach (var t in asm.GetTypes())
        {
            if (!t.IsPublic) continue;
            if (filter != null && t.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;

            var members = new List<string>();
            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (m.IsSpecialName) continue;
                var ps = new List<string>();
                foreach (var pi in m.GetParameters()) ps.Add(pi.ParameterType.Name + " " + pi.Name);
                members.Add($"    {m.ReturnType.Name} {m.Name}({string.Join(", ", ps)})");
            }
            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                members.Add($"    field {f.FieldType.Name} {f.Name}");
            foreach (var pr in t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                members.Add($"    prop {pr.PropertyType.Name} {pr.Name}");

            if (members.Count == 0) continue;
            Console.WriteLine($"=== {t.FullName}");
            members.Sort();
            foreach (var line in members) Console.WriteLine(line);
        }
    }

    static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "poke")
        {
            HavokSystem.Init();
            Poke();
            HavokSystem.Terminate();
            return;
        }

        if (args.Length > 0 && args[0] == "api")
        {
            Api(args.Length > 1 ? args[1] : null);
            return;
        }

        if (args.Length > 0 && args[0] == "states")
        {
            HavokSystem.Init();
            States(args.Length > 1 ? args[1] : "rigrf.xml",
                   args.Length > 2 ? float.Parse(args[2], CultureInfo.InvariantCulture) : 0f,
                   args.Length > 3 ? float.Parse(args[3], CultureInfo.InvariantCulture) : 200f,
                   args.Length > 4 ? int.Parse(args[4]) : 8);
            GC.KeepAlive(Keep);
            HavokSystem.Terminate();
            return;
        }

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

        // Per-child playback speed.  SphereCenturion's two lower rungs are the
        // same clip at 0.026 and 1, not two clips, and sync has to compose with
        // that -- so it is a separate axis from the clip's own duration.
        var pb = new float[ac.m_bindings.Count];
        for (int i = 0; i < pb.Length; i++) pb[i] = 1f;
        string penv = Environment.GetEnvironmentVariable("PB");
        if (penv != null)
        {
            string[] pp = penv.Split(',');
            for (int i = 0; i < pb.Length && i < pp.Length; i++)
                pb[i] = float.Parse(pp[i], CultureInfo.InvariantCulture);
        }

        // Rung positions.  Default 0,1; a floor-rung experiment wants the lowest
        // rung above zero, e.g. WEIGHTS=5,192 as SphereCenturion's forward ladder.
        var rungW = new float[ac.m_bindings.Count];
        for (int i = 0; i < rungW.Length; i++) rungW[i] = i;
        string wenv = Environment.GetEnvironmentVariable("WEIGHTS");
        if (wenv != null)
        {
            string[] parts = wenv.Split(',');
            for (int i = 0; i < rungW.Length && i < parts.Length; i++)
                rungW[i] = float.Parse(parts[i], CultureInfo.InvariantCulture);
        }
        Console.WriteLine("[blend] rung weights " + string.Join(", ", Array.ConvertAll(rungW, F)));

        // How much of each child reaches worldFromModel. Havok blends root motion
        // over weight * worldFromModelWeight, and the corpus only ever uses 1 or 0,
        // so WFM=1,0 asks what a child that is in the pose and not in the motion
        // does. Defaults to 1 everywhere, which is what the shipped files mostly say.
        var rungM = new float[ac.m_bindings.Count];
        for (int i = 0; i < rungM.Length; i++) rungM[i] = 1f;
        string menv = Environment.GetEnvironmentVariable("WFM");
        if (menv != null)
        {
            string[] parts = menv.Split(',');
            for (int i = 0; i < rungM.Length && i < parts.Length; i++)
                rungM[i] = float.Parse(parts[i], CultureInfo.InvariantCulture);
        }
        Console.WriteLine("[blend] worldFromModelWeights " + string.Join(", ", Array.ConvertAll(rungM, F)));

        string denv = Environment.GetEnvironmentVariable("DEAD");
        int dead = denv == null ? -1 : int.Parse(denv);
        // A clip played at p takes duration/p, so that is the rung's duration.
        for (int i = 0; i < pb.Length; i++)
        {
            durs[i] /= pb[i];
            clipSpeed[i] *= pb[i];
            Console.WriteLine($"[blend] rung {i}: pb={F(pb[i])} d={durs[i]:G9} delivers={clipSpeed[i]:G9}");
        }

        var clips = new List<hkbClipGenerator>();
        for (int i = 0; i < ac.m_bindings.Count; i++)
        {
            var clip = Root(new hkbClipGenerator());
            clip.m_name = "clip" + i;
            clip.m_animationName = "clip" + i;
            clip.m_playbackSpeed = pb[i];
            clip.m_animationBindingIndex = -1;
            clip.m_mode = hkbClipGenerator.PlaybackMode.MODE_LOOPING;
            Methods.setAnimationBinding(clip, (IntPtr)ac.m_bindings[i]);

            var kid = Root(new hkbBlenderGeneratorChild());
            kid.m_generator = clip;
            kid.m_weight = rungW[i];
            kid.m_worldFromModelWeight = rungM[i];

            // DEAD=<index> gives that child a state machine with no states at all,
            // so it is in the blend and samples nothing. That is the netch's lower
            // body, and what the engine's Settle claims about it is that its share
            // of the motion goes back to the children that do sample something.
            if (i == dead)
            {
                var empty = Root(new hkbStateMachine());
                empty.m_name = "Empty";
                empty.m_startStateId = 0;
                empty.m_syncVariableIndex = -1;
                empty.m_maxSimultaneousTransitions = 32;

                // One state, and nothing under it. A machine with no states at all
                // faults the runtime, so this is the shape the netch actually has:
                // a branch that is selected and generates nothing.
                var hollow = Root(new hkbStateMachine.StateInfo());
                hollow.m_name = "Hollow";
                hollow.m_stateId = 0;
                hollow.m_probability = 1f;
                hollow.m_enable = true;
                hollow.m_generator = null;
                empty.m_states.Add(hollow);

                kid.m_generator = empty;
                Console.WriteLine("[blend] rung " + i + " samples nothing");
            }

            blender.m_children.Add(kid);
            clips.Add(clip);
        }

        var graph = Root(Methods.createBehavior());
        int soloClip = Environment.GetEnvironmentVariable("SOLO") == null
                     ? -1 : int.Parse(Environment.GetEnvironmentVariable("SOLO"));
        if (soloClip >= 0) { Methods.setRootGenerator(graph, clips[soloClip]); Console.WriteLine("[graph] root = clip " + soloClip); }
        else Methods.setRootGenerator(graph, blender);
        var ctx = Root(new hkbContext());
        // A context built by hand has no project data, and the public generate
        // path walks into it -- that is where setCharacter was faulting.
        ctx.m_projectData = Root(Methods.createProjectData());
        Methods.setCharacter(ctx, ch);
        Methods.setUpVector(ctx, new hkVector4(0, 0, 1, 0));
        Console.WriteLine($"[ctx] projectData={(ctx.m_projectData == null ? "null" : "ok")} " +
                          $"characterSetup={(Methods.getCharacterSetup(ch) == null ? "NULL" : "ok")} " +
                          $"numBones={Methods.getNumBones(ch)}");
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

        // Sweep bounds default to the rung span; XRANGE=lo,hi to go outside it,
        // which is the whole point of a floor-rung measurement.
        double xlo = rungW[0], xhi = rungW[rungW.Length - 1];
        string xenv = Environment.GetEnvironmentVariable("XRANGE");
        if (xenv != null)
        {
            string[] xp = xenv.Split(',');
            xlo = double.Parse(xp[0], CultureInfo.InvariantCulture);
            xhi = double.Parse(xp[1], CultureInfo.InvariantCulture);
        }

        // Section 6 as this document states it: bracket x between two rungs,
        // interpolate travel and duration separately, divide.  Outside the span
        // it clamps to the end rung -- which is the claim under test here.
        Func<double, double> Model = xx =>
        {
            int n = rungW.Length;
            if (xx <= rungW[0]) return clipSpeed[0];
            if (xx >= rungW[n - 1]) return clipSpeed[n - 1];
            int k = 0;
            while (k + 2 < n && rungW[k + 1] <= xx) k++;
            double u = (xx - rungW[k]) / (rungW[k + 1] - rungW[k]);
            double ta = clipSpeed[k] * durs[k], tb = clipSpeed[k + 1] * durs[k + 1];
            return (ta + (tb - ta) * u) / (durs[k] + (durs[k + 1] - durs[k]) * u);
        };

        // XLIST=a,b,c samples exactly those x instead of an even sweep, so a
        // shipped record's own points can be reproduced point for point.
        double[] xs;
        string lenv = Environment.GetEnvironmentVariable("XLIST");
        if (lenv != null)
        {
            string[] lp = lenv.Split(',');
            xs = new double[lp.Length];
            for (int i = 0; i < lp.Length; i++) xs[i] = double.Parse(lp[i], CultureInfo.InvariantCulture);
        }
        else
        {
            xs = new double[steps + 1];
            for (int i = 0; i <= steps; i++) xs[i] = xlo + (xhi - xlo) * i / steps;
        }

        Console.WriteLine("x,delivered,model,err_pct,over_floor");
        for (int st = 0; st < xs.Length; st++)
        {
            double x = xs[st];

            // activate() clones the node tree, so the blend parameter has to be
            // set on the template before the clone is taken.
            blender.m_blendParameter = (float)x;
            Methods.deactivate(graph, ctx);
            Methods.activate(graph, ctx);
            for (int i = 0; i < clips.Count; i++)
                Methods.setAnimationBinding(clips[i], (IntPtr)ac.m_bindings[i]);

            for (int i = 0; i < settle; i++) Step();
            accX = 0; accY = 0;
            for (int i = 0; i < sample; i++) Step();

            double delivered = Math.Sqrt(accX * accX + accY * accY) / (sample * dt);
            double model = Model(x);
            double errPct = model == 0 ? 0 : (delivered - model) / model * 100.0;
            Console.WriteLine($"{x:F4},{delivered:F6},{model:F6},{errPct:+0.0000;-0.0000},"
                            + $"{delivered / clipSpeed[0]:F6}");
            Console.Out.Flush();
        }

        GC.KeepAlive(Keep);
        HavokSystem.Terminate();
    }
}
