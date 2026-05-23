using RiivolutionIsoBuilder.Riivolution;
using System.Xml.Linq;

if (args.Length < 1)
{
    Console.WriteLine("Usage: RiivolutionIsoBuilder.RiivProbe <riivolution.xml> [region] [gameId]");
    Console.WriteLine("Example: dotnet run --project src/RiivolutionIsoBuilder.RiivProbe -- \"..\\Base\\Nueva carpeta\\riivolution\\nmg.xml\" PAL SB4P01");
    Environment.Exit(2);
    return;
}

var xmlFile = Path.GetFullPath(args[0]);
var region = args.Length >= 2 ? args[1] : "PAL";
var gameId = args.Length >= 3 ? args[2] : "SB4P01";

var document = RiivolutionPatchReader.ReadDocument(xmlFile, region);
var plan = RiivolutionPatchPlanner.CreateDefaultPlan(document, gameId);
Console.WriteLine($"XML: {xmlFile}");
Console.WriteLine($"Region: {region}");
Console.WriteLine($"Root: {document.Root}");
Console.WriteLine($"Disc ID: game={document.DiscId?.Game ?? "(any)"} developer={document.DiscId?.Developer ?? "(any)"} regions={string.Join(",", document.DiscId?.Regions ?? [])}");
Console.WriteLine($"Sections: {document.Sections.Count}");
foreach (var section in document.Sections)
{
    Console.WriteLine($"  Section: {section.Name}");
    foreach (var option in section.Options)
    {
        Console.WriteLine($"    Option: {option.Name} default={option.DefaultChoice}");
        for (var index = 0; index < option.Choices.Count; index++)
        {
            var choice = option.Choices[index];
            Console.WriteLine($"      {index + 1}. {choice.Name}: {string.Join(", ", choice.Patches.Select(patch => patch.Id))}");
        }
    }
}

Console.WriteLine($"Patches: {document.Patches.Count}");
Console.WriteLine($"Active patches: {string.Join(", ", plan.ActivePatches.Select(patch => patch.Id))}");
Console.WriteLine();

foreach (var patch in plan.ActivePatches)
{
    Console.WriteLine($"Patch: {patch.Id}");
    Console.WriteLine($"  Root: {patch.Root}");
    Console.WriteLine($"  Savegame: {patch.SaveGame?.External ?? "(none)"} clone={patch.SaveGame?.CloneSave.ToString() ?? "n/a"}");
    Console.WriteLine($"  Folders: {patch.Folders.Count}");
    foreach (var folder in patch.Folders)
    {
        Console.WriteLine($"    {folder.External} -> {folder.Disc} create={folder.Create} recursive={folder.Recursive} resize={folder.Resize} length={folder.Length}");
    }

    Console.WriteLine($"  Memory patches: {patch.MemoryPatches.Count}");
    Console.WriteLine($"    Inline values: {patch.MemoryPatches.Count(memory => memory.Value is not null)}");
    Console.WriteLine($"    Value files: {patch.MemoryPatches.Count(memory => memory.ValueFile is not null)}");
    foreach (var memory in patch.MemoryPatches.Where(memory => memory.ValueFile is not null))
    {
        Console.WriteLine($"      {memory.Offset} <= {memory.ValueFile}");
    }

    Console.WriteLine();
    Console.WriteLine("  Ocarina preview from inline values:");
    var ocarina = OcarinaCodeWriter.Write(patch, gameId, $"{patch.Id} {region}");
    foreach (var line in ocarina.SplitLines().Take(20))
    {
        Console.WriteLine($"    {line}");
    }

    Console.WriteLine();
}

var generatedXml = RiivolutionPatchPlanner.CreateDolPatchXml(plan);
Console.WriteLine("DOLPATCH XML preview:");
foreach (var line in generatedXml.ToString(SaveOptions.DisableFormatting).SplitLines().Take(12))
{
    Console.WriteLine($"  {line}");
}

internal static class StringExtensions
{
    public static IEnumerable<string> SplitLines(this string value)
    {
        using var reader = new StringReader(value);
        while (reader.ReadLine() is { } line)
        {
            yield return line;
        }
    }
}

