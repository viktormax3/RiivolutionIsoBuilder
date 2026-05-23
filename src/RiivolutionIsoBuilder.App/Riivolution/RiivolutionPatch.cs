namespace RiivolutionIsoBuilder.Riivolution;

public sealed record RiivolutionDocument(
    string Root,
    string DisplayName,
    RiivolutionDiscId? DiscId,
    IReadOnlyList<RiivolutionSection> Sections,
    IReadOnlyList<RiivolutionPatch> Patches);

public sealed record RiivolutionDiscId(string Game, string Developer, IReadOnlyList<string> Regions);

public sealed record RiivolutionSection(string Name, IReadOnlyList<RiivolutionOption> Options);

public sealed record RiivolutionOption(
    string Id,
    string Name,
    int DefaultChoice,
    IReadOnlyList<RiivolutionParam> Params,
    IReadOnlyList<RiivolutionChoice> Choices);

public sealed record RiivolutionChoice(
    string Name,
    IReadOnlyList<RiivolutionParam> Params,
    IReadOnlyList<RiivolutionPatchReference> Patches);

public sealed record RiivolutionPatchReference(
    string Id,
    IReadOnlyList<RiivolutionParam> Params);

public sealed record RiivolutionParam(string Name, string Value);

public sealed record RiivolutionPatch(
    string Id,
    string Root,
    RiivolutionSaveGame? SaveGame,
    IReadOnlyList<RiivolutionFileMapping> Files,
    IReadOnlyList<RiivolutionFolderMapping> Folders,
    IReadOnlyList<RiivolutionMemoryPatch> MemoryPatches);

public sealed record RiivolutionSaveGame(string External, bool CloneSave);

public sealed record RiivolutionFileMapping(string External, string Disc, bool Resize, bool Create, string Offset, string Length);

public sealed record RiivolutionFolderMapping(string External, string Disc, bool Resize, bool Create, bool Recursive, string Length);

public sealed record RiivolutionMemoryPatch(
    string Tag,
    string Offset,
    string? Value,
    string? ValueFile,
    string? Original);

