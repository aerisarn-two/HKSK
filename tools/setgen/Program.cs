using System.Diagnostics;
using System.Globalization;
using HKSK.Cache;
using HKSK.Model;
using HKSK.SetData;
using HKSK.SetGen;

// setgen: writes animationsetdatasinglefile.txt from the game's other assets.
//
//   setgen <meshes> <data> [-o <output>] [--slack <factor>] [--force]
//
// <meshes> is the extracted meshes folder -- animationdatasinglefile.txt and the actors'
// behaviour and character files. <data> is the game's Data folder, for the five masters.
// A shipped animationsetdatasinglefile.txt in <meshes> is emptied before anything is built.

string? meshes = null, data = null, output = null;
bool force = false;
double slack = SetDataGenerator.DefaultSlack;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "-o" or "--output" when i + 1 < args.Length:
            output = args[++i];
            break;
        case "--slack" when i + 1 < args.Length:
            if (!double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out slack) || !(slack >= 1))
                return Fail($"--slack wants a number of at least 1, not '{args[i]}'");
            break;
        case "--force":
            force = true;
            break;
        case "-h" or "--help":
            return Usage();
        default:
            if (args[i].StartsWith('-')) return Fail($"unknown option '{args[i]}'");
            if (meshes is null) meshes = args[i];
            else if (data is null) data = args[i];
            else return Fail($"unexpected argument '{args[i]}'");
            break;
    }
}

if (meshes is null || data is null) return Usage();

meshes = Path.GetFullPath(meshes);
data = Path.GetFullPath(data);

if (!File.Exists(Path.Combine(meshes, SkyrimCache.AnimationDataFileName)))
    return Fail($"{meshes} has no {SkyrimCache.AnimationDataFileName}: pass the extracted meshes folder");
if (!File.Exists(Path.Combine(data, MasterData.Order[0])))
    return Fail($"{data} has no {MasterData.Order[0]}: pass the game's Data folder");

output = Path.GetFullPath(output ?? SkyrimCache.AnimationSetDataFileName);
if (Directory.Exists(output)) output = Path.Combine(output, SkyrimCache.AnimationSetDataFileName);

string vanilla = Path.Combine(meshes, SkyrimCache.AnimationSetDataFileName);
if (!force && string.Equals(output, vanilla, StringComparison.OrdinalIgnoreCase) && File.Exists(vanilla))
    return Fail($"{output} is the shipped file in the input folder; choose another output or pass --force");

var clock = Stopwatch.StartNew();

SkyrimCache cache = SkyrimCache.Load(meshes);
// Built from the other assets alone: whatever a shipped set data file says is dropped
// before anything can ask it.
cache.SetData.Projects.Clear();
Console.WriteLine($"cache      {meshes}");

GameEvents events = MasterData.Events(data);
Console.WriteLine($"masters    {data}  ({events.Idle.Count} idle events, {events.Equip.Count} equip, {events.Attacks.Count} graphs with races)");

AnimationSetDataFile file = SetDataGenerator.Generate(cache, events, slack);

Directory.CreateDirectory(Path.GetDirectoryName(output)!);
file.Save(output);

int sets = file.Projects.Sum(p => p.Sets.Sets.Count);
int animations = file.Projects.Sum(p => p.Sets.Sets.Sum(s => s.Checksums.Entries.Count / 3));
int attacks = file.Projects.Sum(p => p.Sets.Sets.Sum(s => s.Attacks.Attacks.Count));

Console.WriteLine($"projects   {file.Projects.Count}");
Console.WriteLine($"sets       {sets}  ({animations} animations listed, {attacks} attacks)");
Console.WriteLine($"wrote      {output}  ({new FileInfo(output).Length} bytes, {clock.Elapsed.TotalSeconds:0.0}s)");
return 0;

static int Usage()
{
    Console.Error.WriteLine("usage: setgen <meshes> <data> [-o <output>] [--slack <factor>] [--force]");
    Console.Error.WriteLine("  <meshes>      extracted meshes folder (animationdatasinglefile.txt, behaviours)");
    Console.Error.WriteLine("  <data>        the game's Data folder (Skyrim.esm and the DLC masters)");
    Console.Error.WriteLine("  -o <output>   file or folder to write; default ./animationsetdatasinglefile.txt");
    Console.Error.WriteLine($"  --slack       how far a set covering several weapons may outgrow one; default {SetDataGenerator.DefaultSlack.ToString(CultureInfo.InvariantCulture)}");
    Console.Error.WriteLine("  --force       allow overwriting the shipped file in <meshes>");
    return 2;
}

static int Fail(string message)
{
    Console.Error.WriteLine($"setgen: {message}");
    return 1;
}
