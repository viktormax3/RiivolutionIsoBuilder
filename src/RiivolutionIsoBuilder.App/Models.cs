using RiivolutionIsoBuilder.Riivolution;

namespace RiivolutionIsoBuilder;

public sealed record GameImage(string Path, string GameId, GameDefinition Game, RegionDefinition Region)
{
    public string DisplayName => $"{GameId} - {Game.DisplayName} ({Region.Name})";
}

public sealed class CatalogDefinition
{
    public List<GameDefinition> Games { get; set; } = [];
    public List<RegionDefinition> Regions { get; set; } = [];
    public List<ModDefinition> Mods { get; set; } = [];
}

public sealed class GameDefinition
{
    public string Key { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string[] GameIds { get; set; } = [];
    public long RequiredFreeSpaceGb { get; set; }
}

public sealed class RegionDefinition
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string IdCharacter { get; set; } = "";
}

public sealed class ModDefinition
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string GameKey { get; set; } = "";
    public string? Archive { get; set; }
    public string? ExtractedFolder { get; set; }
    public string? OutputIdPrefix { get; set; }
    public PatchKind DefaultPatch { get; set; }
    public string? PatchFile { get; set; }
    public string? BannerId { get; set; }
    public string[] UnsupportedOutputIds { get; set; } = [];
    public Dictionary<string, PatchKind> PatchOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public enum PatchKind
{
    None,
    Gct,
    Xml
}

public sealed record BuildOptions(string Extension, bool UseCustomBanner);

public sealed record BuildPlan(
    GameImage Game,
    ModDefinition Mod,
    string OutputId,
    string Tmd,
    RegionDefinition Region,
    PatchKind PatchKind,
    string ModRiiv,
    string PatchFile,
    string ModDirectory,
    string WorkDirectory,
    string OutputFile);

public sealed record NativeRiivolutionMod(
    string XmlFile,
    string SourceRoot,
    RiivolutionDocument Document,
    RiivolutionPlan Plan,
    string ChoiceSummary)
{
    public string DisplayName
    {
        get
        {
            var patches = string.Join(", ", Plan.ActivePatches.Select(patch => patch.Id).Where(id => id.Length > 0));
            return patches.Length == 0 ? Document.DisplayName : $"{Document.DisplayName} ({patches})";
        }
    }
    
    public string ShortName => Document.DisplayName;
}

public sealed record ManualGctPatch(string PatchFile, string DisplayName);

public sealed record NativeBuildPlan(
    GameImage Game,
    NativeRiivolutionMod Mod,
    string OutputId,
    string Tmd,
    string InternalName,
    string WorkDirectory,
    string OutputFile);

public sealed record GctBuildPlan(
    GameImage Game,
    ManualGctPatch Patch,
    string OutputId,
    string Tmd,
    string WorkDirectory,
    string OutputFile);

