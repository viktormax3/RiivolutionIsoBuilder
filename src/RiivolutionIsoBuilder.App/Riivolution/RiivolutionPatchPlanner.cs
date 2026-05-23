using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace RiivolutionIsoBuilder.Riivolution;

public sealed record RiivolutionPlan(
    IReadOnlyList<RiivolutionPatch> ActivePatches,
    IReadOnlyDictionary<string, string> Parameters);

public static partial class RiivolutionPatchPlanner
{
    public static RiivolutionPlan CreateDefaultPlan(RiivolutionDocument document, string gameId)
    {
        return CreatePlan(document, gameId, []);
    }

    public static RiivolutionPlan CreatePlan(RiivolutionDocument document, string gameId, IReadOnlyList<int?> choiceIndexes)
    {
        var parameters = BuiltInParameters(gameId);
        var activeIds = new List<string>();
        var optionIndex = 0;

        foreach (var option in document.Sections.SelectMany(section => section.Options))
        {
            var selectedChoice = optionIndex < choiceIndexes.Count ? choiceIndexes[optionIndex] : null;
            var choiceIndex = selectedChoice ?? (option.DefaultChoice > 0 ? option.DefaultChoice - 1 : -1);
            optionIndex++;
            if (choiceIndex < 0 || choiceIndex >= option.Choices.Count)
            {
                continue;
            }

            foreach (var param in option.Params)
            {
                parameters[param.Name] = Resolve(param.Value, parameters);
            }

            var choice = option.Choices[choiceIndex];
            foreach (var param in choice.Params)
            {
                parameters[param.Name] = Resolve(param.Value, parameters);
            }

            foreach (var patch in choice.Patches)
            {
                foreach (var param in patch.Params)
                {
                    parameters[param.Name] = Resolve(param.Value, parameters);
                }

                activeIds.Add(patch.Id);
            }
        }

        var activePatches = document.Patches
            .Where(patch => activeIds.Contains(patch.Id, StringComparer.OrdinalIgnoreCase))
            .ToList();

        var hasChoicePatchReferences = document.Sections
            .SelectMany(section => section.Options)
            .SelectMany(option => option.Choices)
            .Any(choice => choice.Patches.Count > 0);
        if (activePatches.Count == 0 && document.Patches.Count == 1 && !hasChoicePatchReferences)
        {
            activePatches.Add(document.Patches[0]);
        }

        return new RiivolutionPlan(activePatches, parameters);
    }

    public static string ResolvePath(string value, IReadOnlyDictionary<string, string> parameters)
    {
        return Resolve(value, parameters).Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
    }

    public static XDocument CreateDolPatchXml(RiivolutionPlan plan)
    {
        return CreateDolPatchXml(plan, null);
    }

    public static XDocument CreateDolPatchXml(RiivolutionPlan plan, string? mainDol)
    {
        var root = new XElement("wiidisc", new XAttribute("version", "1"));

        foreach (var patch in plan.ActivePatches)
        {
            foreach (var memory in patch.MemoryPatches)
            {
                if (mainDol is not null && !OriginalMatches(mainDol, memory))
                {
                    continue;
                }

                var element = new XElement("memory", new XAttribute("offset", $"0x{memory.Offset}"));
                if (memory.ValueFile is not null)
                {
                    var valueFile = ResolvePatchRelativePath(patch.Root, Resolve(memory.ValueFile, plan.Parameters));
                    element.SetAttributeValue("valuefile", valueFile.Replace('\\', '/'));
                }
                else if (memory.Value is not null)
                {
                    element.SetAttributeValue("value", memory.Value);
                }

                if (!string.IsNullOrWhiteSpace(memory.Original))
                {
                    element.SetAttributeValue("original", memory.Original);
                }

                root.Add(element);
            }
        }

        return new XDocument(root);
    }

    private static Dictionary<string, string> BuiltInParameters(string gameId)
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["__gameid"] = gameId.Length >= 3 ? gameId[..3] : gameId,
            ["__region"] = gameId.Length >= 4 ? gameId[3].ToString() : "",
            ["__maker"] = gameId.Length >= 6 ? gameId[4..6] : ""
        };
    }

    private static string Resolve(string value, IReadOnlyDictionary<string, string> parameters)
    {
        return ParameterRegex().Replace(value, match =>
            parameters.TryGetValue(match.Groups[1].Value, out var replacement) ? replacement : "");
    }

    private static string ResolvePatchRelativePath(string patchRoot, string value)
    {
        if (value.StartsWith('/') || value.StartsWith('\\'))
        {
            return value.TrimStart('/', '\\');
        }

        var normalizedRoot = patchRoot.Trim('/', '\\');
        var normalizedValue = value.TrimStart('/', '\\');
        return normalizedRoot.Length == 0 ? normalizedValue : Path.Combine(normalizedRoot, normalizedValue);
    }

    private static bool OriginalMatches(string mainDol, RiivolutionMemoryPatch memory)
    {
        if (string.IsNullOrWhiteSpace(memory.Original))
        {
            return true;
        }

        if (!TryMapDolAddress(mainDol, Convert.ToUInt32(memory.Offset, 16), out var fileOffset))
        {
            return true;
        }

        var original = Convert.FromHexString(memory.Original);
        using var stream = File.OpenRead(mainDol);
        if (fileOffset < 0 || fileOffset + original.Length > stream.Length)
        {
            return false;
        }

        stream.Position = fileOffset;
        var actual = new byte[original.Length];
        return stream.Read(actual, 0, actual.Length) == actual.Length && actual.SequenceEqual(original);
    }

    private static bool TryMapDolAddress(string mainDol, uint address, out long fileOffset)
    {
        Span<byte> header = stackalloc byte[0x100];
        using var stream = File.OpenRead(mainDol);
        if (stream.Read(header) < header.Length)
        {
            fileOffset = 0;
            return false;
        }

        for (var i = 0; i < 18; i++)
        {
            var offset = ReadU32(header[(i * 4)..]);
            var loadAddress = ReadU32(header[(0x48 + i * 4)..]);
            var size = ReadU32(header[(0x90 + i * 4)..]);
            if (size == 0 || address < loadAddress || address >= loadAddress + size)
            {
                continue;
            }

            fileOffset = offset + (address - loadAddress);
            return true;
        }

        fileOffset = 0;
        return false;
    }

    private static uint ReadU32(ReadOnlySpan<byte> data)
    {
        return ((uint)data[0] << 24) | ((uint)data[1] << 16) | ((uint)data[2] << 8) | data[3];
    }

    [GeneratedRegex(@"\{\$([^}]+)\}")]
    private static partial Regex ParameterRegex();
}

