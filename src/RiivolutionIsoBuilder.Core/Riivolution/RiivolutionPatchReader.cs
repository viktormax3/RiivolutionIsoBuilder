using System.Xml.Linq;

namespace RiivolutionIsoBuilder.Riivolution;

public static class RiivolutionPatchReader
{
    private static readonly HashSet<string> NonPatchTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "wiidisc",
        "id",
        "options",
        "option",
        "choice",
        "patch",
        "folder",
        "file",
        "savegame",
        "section"
    };

    public static RiivolutionDocument ReadDocument(string xmlFile, string regionName)
    {
        var document = XDocument.Load(xmlFile, LoadOptions.PreserveWhitespace);
        var root = document.Root is null ? "" : (string?)document.Root.Attribute("root") ?? "/riivolution";
        var discId = ReadDiscId(document);
        var sections = document
            .DescendantsLocal("section")
            .Select(ReadSection)
            .ToList();
        var patches = document
            .DescendantsLocal("patch")
            .Where(IsPatchDefinition)
            .Select(patch => ReadPatch(patch, regionName, root))
            .ToList();
        var displayName = ResolveDisplayName(xmlFile, document, sections, patches);

        return new RiivolutionDocument(root, displayName, discId, sections, patches);
    }

    public static RiivolutionPatch Read(string xmlFile, string regionName)
    {
        var document = ReadDocument(xmlFile, regionName);
        return document.Patches.FirstOrDefault()
            ?? new RiivolutionPatch(Path.GetFileNameWithoutExtension(xmlFile), "", null, [], [], []);
    }

    private static RiivolutionDiscId? ReadDiscId(XDocument document)
    {
        var id = document.Root?.ElementsLocal("id").FirstOrDefault();
        if (id is null)
        {
            return null;
        }

        return new RiivolutionDiscId(
            (string?)id.Attribute("game") ?? "",
            (string?)id.Attribute("developer") ?? "",
            id.ElementsLocal("region").Select(region => (string?)region.Attribute("type") ?? "").Where(type => type.Length > 0).ToList());
    }

    private static string ResolveDisplayName(string xmlFile, XDocument document, IReadOnlyList<RiivolutionSection> sections, IReadOnlyList<RiivolutionPatch> patches)
    {
        var rootName = (string?)document.Root?.Attribute("name")
            ?? (string?)document.Root?.Attribute("title");
        if (!string.IsNullOrWhiteSpace(rootName))
        {
            return rootName;
        }

        var sectionName = sections.Select(section => section.Name).FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));
        if (!string.IsNullOrWhiteSpace(sectionName))
        {
            return sectionName;
        }

        var patchId = patches.Select(patch => patch.Id).FirstOrDefault(id => !string.IsNullOrWhiteSpace(id));
        if (!string.IsNullOrWhiteSpace(patchId))
        {
            return patchId;
        }

        return Path.GetFileNameWithoutExtension(xmlFile);
    }

    private static RiivolutionSection ReadSection(XElement section)
    {
        return new RiivolutionSection(
            (string?)section.Attribute("name") ?? "",
            section.ElementsLocal("option").Select(ReadOption).ToList());
    }

    private static RiivolutionOption ReadOption(XElement option)
    {
        return new RiivolutionOption(
            (string?)option.Attribute("id") ?? "",
            (string?)option.Attribute("name") ?? "",
            ParseInt((string?)option.Attribute("default") ?? (string?)option.Attribute("index")),
            ReadParams(option),
            option.ElementsLocal("choice").Select(ReadChoice).ToList());
    }

    private static RiivolutionChoice ReadChoice(XElement choice)
    {
        return new RiivolutionChoice(
            (string?)choice.Attribute("name") ?? "",
            ReadParams(choice),
            choice.ElementsLocal("patch")
                .Select(patch => new RiivolutionPatchReference((string?)patch.Attribute("id") ?? "", ReadParams(patch)))
                .Where(patch => patch.Id.Length > 0)
                .ToList());
    }

    private static RiivolutionPatch ReadPatch(XElement patch, string regionName, string documentRoot)
    {
        var id = (string?)patch.Attribute("id") ?? "";
        var root = (string?)patch.Attribute("root") ?? documentRoot;
        var saveGame = patch.ElementsLocal("savegame")
            .Select(element => new RiivolutionSaveGame(
                (string?)element.Attribute("external") ?? "",
                ParseBool((string?)element.Attribute("clone"))))
            .FirstOrDefault();
        var files = patch.ElementsLocal("file")
            .Select(element => new RiivolutionFileMapping(
                (string?)element.Attribute("external") ?? "",
                (string?)element.Attribute("disc") ?? "",
                ParseBool((string?)element.Attribute("resize"), defaultValue: true),
                ParseBool((string?)element.Attribute("create")),
                (string?)element.Attribute("offset") ?? "",
                (string?)element.Attribute("length") ?? ""))
            .ToList();
        var folders = patch.ElementsLocal("folder")
            .Select(element => new RiivolutionFolderMapping(
                (string?)element.Attribute("external") ?? "",
                (string?)element.Attribute("disc") ?? "",
                ParseBool((string?)element.Attribute("resize"), defaultValue: true),
                ParseBool((string?)element.Attribute("create")),
                ParseBool((string?)element.Attribute("recursive"), defaultValue: true),
                (string?)element.Attribute("length") ?? ""))
            .ToList();
        var memoryPatches = patch
            .Descendants()
            .Where(element => HasPatchShape(element) && IsForRegion(element, regionName))
            .Select(element => new RiivolutionMemoryPatch(
                element.Name.LocalName,
                NormalizeHex((string?)element.Attribute("offset")) ?? "",
                NormalizeHex((string?)element.Attribute("value")),
                (string?)element.Attribute("valuefile"),
                NormalizeHex((string?)element.Attribute("original"))))
            .Where(memory => memory.Offset.Length > 0 && (memory.Value is not null || memory.ValueFile is not null))
            .ToList();

        return new RiivolutionPatch(id, root, saveGame, files, folders, memoryPatches);
    }

    private static bool HasPatchShape(XElement element)
    {
        if (NonPatchTags.Contains(element.Name.LocalName))
        {
            return false;
        }

        return element.Attribute("offset") is not null
            && (element.Attribute("value") is not null || element.Attribute("valuefile") is not null);
    }

    private static bool IsPatchDefinition(XElement patch)
    {
        return patch.Attribute("root") is not null
            || patch.Elements().Any(element =>
                element.Name.LocalName.Equals("folder", StringComparison.OrdinalIgnoreCase)
                || element.Name.LocalName.Equals("memory", StringComparison.OrdinalIgnoreCase)
                || element.Name.LocalName.Equals("savegame", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsForRegion(XElement element, string regionName)
    {
        return element.Name.LocalName.Equals("memory", StringComparison.OrdinalIgnoreCase)
            || element.Name.LocalName.Equals(regionName, StringComparison.OrdinalIgnoreCase)
            || !IsKnownRegionTag(element.Name.LocalName);
    }

    private static bool IsKnownRegionTag(string tag)
    {
        return tag.Equals("USA", StringComparison.OrdinalIgnoreCase)
            || tag.Equals("PAL", StringComparison.OrdinalIgnoreCase)
            || tag.Equals("JAP", StringComparison.OrdinalIgnoreCase)
            || tag.Equals("EUR", StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeHex(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value[2..] : value;
    }

    private static IReadOnlyList<RiivolutionParam> ReadParams(XElement element)
    {
        return element.ElementsLocal("param")
            .Select(param => new RiivolutionParam((string?)param.Attribute("name") ?? "", (string?)param.Attribute("value") ?? ""))
            .Where(param => param.Name.Length > 0)
            .ToList();
    }

    private static bool ParseBool(string? value, bool defaultValue = false)
    {
        if (value is null)
        {
            return defaultValue;
        }

        return value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    private static int ParseInt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        return value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? Convert.ToInt32(value[2..], 16)
            : int.Parse(value);
    }
}

internal static class XmlLocalNameExtensions
{
    public static IEnumerable<XElement> ElementsLocal(this XContainer element, string localName)
    {
        return element.Elements().Where(child => child.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase));
    }

    public static IEnumerable<XElement> DescendantsLocal(this XContainer element, string localName)
    {
        return element.Descendants().Where(child => child.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase));
    }
}
