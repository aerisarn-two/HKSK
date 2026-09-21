using System.Diagnostics;
using System.Globalization;
using HKSK.Cache;
using HKSK.Model;
using HKSK.Speed;
using HKSK.Records;
using HKSK.Tools;

// speedgen: writes speeddatasinglefile.txt from the game's other assets.
//
//   speedgen <meshes> <data> [-o <output>] [--tolerance <units>] [--force]
//
// <meshes> is the extracted meshes folder -- animationdatasinglefile.txt, the split
// cache under animationdata/, and the actors' behaviour files. <data> is the game's
// Data folder, for the five masters. A shipped speeddatasinglefile.txt in <meshes> is
// never read.

string? meshes = null, data = null, output = null;
float tolerance = SpeedDataGenerator.DefaultTolerance;
bool force = false;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "-o" or "--output" when i + 1 < args.Length:
            output = args[++i];
            break;
        case "--tolerance" when i + 1 < args.Length:
            if (!float.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out tolerance) || !(tolerance > 0f))
                return Fail($"--tolerance wants a positive number, not '{args[i]}'");
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
if (!File.Exists(Path.Combine(data, MasterRecords.Order[0])))
    return Fail($"{data} has no {MasterRecords.Order[0]}: pass the game's Data folder");

output = Path.GetFullPath(output ?? SpeedDataFile.FileName);
if (Directory.Exists(output)) output = Path.Combine(output, SpeedDataFile.FileName);

string vanilla = Path.Combine(meshes, SpeedDataFile.FileName);
if (!force && string.Equals(output, vanilla, StringComparison.OrdinalIgnoreCase) && File.Exists(vanilla))
    return Fail($"{output} is the shipped table in the input folder; choose another output or pass --force");

var clock = Stopwatch.StartNew();

SkyrimCache cache = SkyrimCache.Load(meshes);
// The table is written from the other assets alone. Whatever a shipped one says is
// dropped before anything can ask it.
cache.SpeedData = null;
Console.WriteLine($"cache      {meshes}");

var movements = GameRecordRules.MovementTypes(MasterRecords.Read(data));
Console.WriteLine($"masters    {data}  ({movements.Count} movement types)");

SpeedDataFile file = SpeedDataGenerator.Generate(cache, movements, tolerance);

Directory.CreateDirectory(Path.GetDirectoryName(output)!);
file.Save(output);

int blocks = file.Blocks.Sum(b => b.Entries.Count);
int records = file.Blocks.Sum(b => b.Entries.Sum(e => e.Records.Count));
int points = file.Blocks.Sum(b => b.Entries.Sum(e => e.Records.Sum(r => r.Points.Count)));

Console.WriteLine($"projects   {file.Projects.Count}");
Console.WriteLine($"blocks     {blocks}  ({records} records, {points} points, tolerance {tolerance.ToString(CultureInfo.InvariantCulture)})");
Console.WriteLine($"wrote      {output}  ({new FileInfo(output).Length} bytes, {clock.Elapsed.TotalSeconds:0.0}s)");
return 0;

static int Usage()
{
    Console.Error.WriteLine("usage: speedgen <meshes> <data> [-o <output>] [--tolerance <units>] [--force]");
    Console.Error.WriteLine("  <meshes>      extracted meshes folder (animationdatasinglefile.txt, behaviours)");
    Console.Error.WriteLine("  <data>        the game's Data folder (Skyrim.esm and the DLC masters)");
    Console.Error.WriteLine("  -o <output>   file or folder to write; default ./speeddatasinglefile.txt");
    Console.Error.WriteLine($"  --tolerance   how far a dropped point may sit from its line; default {SpeedDataGenerator.DefaultTolerance.ToString(CultureInfo.InvariantCulture)}; the game's own files used 2");
    Console.Error.WriteLine("  --force       allow overwriting the shipped table in <meshes>");
    return 2;
}

static int Fail(string message)
{
    Console.Error.WriteLine($"speedgen: {message}");
    return 1;
}
