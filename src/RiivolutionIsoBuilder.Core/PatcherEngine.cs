using System.Text;
using System.Xml.Linq;
using RiivolutionIsoBuilder.Riivolution;

namespace RiivolutionIsoBuilder;

public sealed class PatcherEngine
{
    private readonly PatcherPaths paths;
    private readonly ModCatalog catalog;
    private readonly IWiiToolchain toolchain;
    private readonly ArchiveExtractor extractor;
    private readonly Action<string> log;

    public PatcherEngine(PatcherPaths paths, Action<string> log)
        : this(paths, log, new WiimmToolchain(paths, log))
    {
    }

    public PatcherEngine(PatcherPaths paths, Action<string> log, IWiiToolchain toolchain)
    {
        this.paths = paths;
        this.log = log;
        this.toolchain = toolchain;
        catalog = ModCatalog.Load(paths.ResolveCatalogFile());
        extractor = new ArchiveExtractor(log);
    }

    public async Task<GameImage?> InspectImageAsync(string imagePath, CancellationToken cancellationToken)
    {
        var result = await toolchain.InspectImageAsync(imagePath, cancellationToken);
        if (result.ExitCode != 0)
        {
            return null;
        }

        return ParseWitList(result.Output)
            .Select(CreateGameImage)
            .FirstOrDefault();
    }

    public async Task<IReadOnlyList<GameImage>> ScanAsync(CancellationToken cancellationToken)
    {
        var found = new List<GameImage>();
        foreach (var searchDirectory in paths.GameSearchDirectories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(searchDirectory))
            {
                continue;
            }

            var result = await toolchain.ScanDirectoryAsync(searchDirectory, cancellationToken);
            if (result.ExitCode != 0)
            {
                continue;
            }

            foreach (var image in ParseWitList(result.Output).Select(CreateGameImage))
            {
                if (found.All(x => !Path.GetFullPath(x.Path).Equals(Path.GetFullPath(image.Path), StringComparison.OrdinalIgnoreCase)))
                {
                    found.Add(image);
                }
            }
        }

        return found;
    }

    public IReadOnlyList<ModDefinition> GetAvailableMods(GameDefinition game)
    {
        return catalog.GetModsForGame(game.Key)
            .Where(mod => File.Exists(ResolveModArchive(mod)))
            .ToList();
    }

    public NativeRiivolutionMod LoadNativeRiivolutionMod(string xmlFile, GameImage game, IReadOnlyList<int?> choiceIndexes, RiivolutionDocument? document = null)
    {
        document ??= RiivolutionPatchReader.ReadDocument(xmlFile, game.Region.Name);
        ValidateNativeDocumentForGame(document, game);
        var plan = RiivolutionPatchPlanner.CreatePlan(document, game.GameId, choiceIndexes);
        var sourceRoot = ResolveNativeSourceRoot(xmlFile);
        var choiceSummary = CreateChoiceSummary(document, choiceIndexes);
        return new NativeRiivolutionMod(xmlFile, sourceRoot, document, plan, choiceSummary);
    }

    public string SuggestNativeOutputId(NativeRiivolutionMod mod, GameImage game)
    {
        return OutputIdSuggester.ForNativeRiivolutionMod(mod, game);
    }

    public BuildPlan CreatePlan(GameImage game, ModDefinition mod, BuildOptions options)
    {
        var suffix = game.GameId[3..6];
        var outputId = OutputIdSuggester.ForCatalogMod(mod, game);
        if (suffix.StartsWith('K') || mod.UnsupportedOutputIds.Contains(outputId, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Este mod no esta disponible para la region seleccionada.");
        }

        var overrideEntry = mod.PatchOverrides.FirstOrDefault(pair =>
            pair.Key.Equals(outputId, StringComparison.OrdinalIgnoreCase));
        var patch = overrideEntry.Key is not null
            ? overrideEntry.Value
            : mod.DefaultPatch;

        var outputFolder = Path.Combine(paths.OutputDirectory, $"{mod.DisplayName} [{outputId}]");
        var outputFile = Path.Combine(outputFolder, $"{outputId}.{options.Extension}");
        var extractRoot = Path.Combine(paths.TempDirectory, "mods");
        var archive = ResolveModArchive(mod);
        var patchFile = ResolvePatchFile(mod, patch, outputId);
        return new BuildPlan(
            game,
            mod,
            outputId,
            outputId[..4],
            catalog.GetRegion(outputId),
            patch,
            archive,
            patchFile,
            Path.Combine(extractRoot, mod.ExtractedFolder ?? mod.Id),
            Path.Combine(paths.TempDirectory, $"{outputId}-FST"),
            outputFile);
    }

    public async Task BuildAsync(BuildPlan plan, BuildOptions options, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(plan.OutputFile)!);
        VerifyFreeSpace(plan.Game.Game);

        DeleteIfExists(plan.ModDirectory);
        DeleteIfExists(plan.WorkDirectory);
        if (File.Exists(plan.OutputFile))
        {
            File.Delete(plan.OutputFile);
        }

        log("Descomprimiendo mod Riivolution...");
        DeleteIfExists(Path.Combine(paths.TempDirectory, "mods"));
        await extractor.ExtractAsync(plan.ModRiiv, Path.Combine(paths.TempDirectory, "mods"), cancellationToken);

        log("Extrayendo datos del juego con wit...");
        (await toolchain.ExtractDataPartitionAsync(plan.Game.Path, plan.WorkDirectory, cancellationToken))
            .EnsureSuccess("No se pudo extraer la imagen del juego.");

        await ApplyPatchAsync(plan, cancellationToken);
        CopyModFiles(plan, options);

        log("Creando imagen modificada...");
        (await toolchain.CreateImageAsync(plan.WorkDirectory, plan.OutputFile, cancellationToken))
            .EnsureSuccess("No se pudo crear la imagen modificada.");

        log("Editando ID, TMD y nombre interno...");
        (await toolchain.EditImageMetadataAsync(plan.OutputFile, plan.OutputId, plan.Mod.DisplayName, plan.Tmd, cancellationToken))
            .EnsureSuccess("No se pudo editar la metadata de salida.");

        DeleteIfExists(paths.TempDirectory);
        log($"Listo: {plan.OutputFile}");
    }

    public NativeBuildPlan CreateNativePlan(GameImage game, NativeRiivolutionMod mod, string outputId, BuildOptions options)
    {
        outputId = OutputIdSuggester.Normalize(outputId);
        var outputFolder = Path.Combine(paths.OutputDirectory, $"{mod.DisplayName} [{outputId}]");
        return new NativeBuildPlan(
            game,
            mod,
            outputId,
            outputId[..4],
            mod.DisplayName,
            Path.Combine(paths.TempDirectory, $"{outputId}-FST"),
            Path.Combine(outputFolder, $"{outputId}.{options.Extension}"));
    }

    public GctBuildPlan CreateGctPlan(GameImage game, ManualGctPatch patch, string outputId, BuildOptions options)
    {
        outputId = OutputIdSuggester.Normalize(outputId);
        var outputFolder = Path.Combine(paths.OutputDirectory, $"{patch.DisplayName} [{outputId}]");
        return new GctBuildPlan(
            game,
            patch,
            outputId,
            outputId[..4],
            Path.Combine(paths.TempDirectory, $"{outputId}-FST"),
            Path.Combine(outputFolder, $"{outputId}.{options.Extension}"));
    }

    public async Task BuildNativeAsync(NativeBuildPlan plan, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(plan.OutputFile)!);
        VerifyFreeSpace(plan.Game.Game);
        DeleteIfExists(plan.WorkDirectory);
        if (File.Exists(plan.OutputFile))
        {
            File.Delete(plan.OutputFile);
        }

        log("Extrayendo datos del juego con wit...");
        (await toolchain.ExtractDataPartitionAsync(plan.Game.Path, plan.WorkDirectory, cancellationToken))
            .EnsureSuccess("No se pudo extraer la imagen del juego.");

        CopyNativeFiles(plan.Mod, plan.WorkDirectory);
        await ApplyNativeDolPatchAsync(plan, cancellationToken);

        log("Creando imagen modificada...");
        (await toolchain.CreateImageAsync(plan.WorkDirectory, plan.OutputFile, cancellationToken))
            .EnsureSuccess("No se pudo crear la imagen modificada.");

        log("Editando ID, TMD y nombre interno...");
        (await toolchain.EditImageMetadataAsync(plan.OutputFile, plan.OutputId, plan.InternalName, plan.Tmd, cancellationToken))
            .EnsureSuccess("No se pudo editar la metadata de salida.");

        DeleteIfExists(paths.TempDirectory);
        log($"Listo: {plan.OutputFile}");
    }

    public async Task BuildGctAsync(GctBuildPlan plan, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(plan.OutputFile)!);
        VerifyFreeSpace(plan.Game.Game);
        DeleteIfExists(plan.WorkDirectory);
        if (File.Exists(plan.OutputFile))
        {
            File.Delete(plan.OutputFile);
        }

        log("Extrayendo datos del juego con wit...");
        (await toolchain.ExtractDataPartitionAsync(plan.Game.Path, plan.WorkDirectory, cancellationToken))
            .EnsureSuccess("No se pudo extraer la imagen del juego.");

        await ApplyGctPatchAsync(Path.Combine(plan.WorkDirectory, "sys", "main.dol"), plan.Patch.PatchFile, cancellationToken);

        log("Creando imagen modificada...");
        (await toolchain.CreateImageAsync(plan.WorkDirectory, plan.OutputFile, cancellationToken))
            .EnsureSuccess("No se pudo crear la imagen modificada.");

        log("Editando ID, TMD y nombre interno...");
        (await toolchain.EditImageMetadataAsync(plan.OutputFile, plan.OutputId, plan.Patch.DisplayName, plan.Tmd, cancellationToken))
            .EnsureSuccess("No se pudo editar la metadata de salida.");

        DeleteIfExists(paths.TempDirectory);
        log($"Listo: {plan.OutputFile}");
    }

    private async Task ApplyPatchAsync(BuildPlan plan, CancellationToken cancellationToken)
    {
        if (plan.PatchKind == PatchKind.None)
        {
            return;
        }

        var mainDol = Path.Combine(plan.WorkDirectory, "sys", "main.dol");
        if (plan.PatchKind == PatchKind.Gct)
        {
            await ApplyGctPatchAsync(mainDol, plan.PatchFile, cancellationToken);
            return;
        }

        var xmlFile = plan.PatchFile;
        if (!File.Exists(xmlFile))
        {
            throw new FileNotFoundException("No se encontro el parche XML.", xmlFile);
        }

        Directory.CreateDirectory(paths.TempDirectory);
        var tempXml = Path.Combine(paths.TempDirectory, $"{plan.Mod.Id}.xml.tmp");
        var xml = File.ReadAllText(xmlFile, Encoding.UTF8).Replace(plan.Region.Name, "memory", StringComparison.OrdinalIgnoreCase);
        await File.WriteAllTextAsync(tempXml, xml, Encoding.UTF8, cancellationToken);
        try
        {
            log("Aplicando parche XML con wit DOLPATCH...");
            (await toolchain.ApplyDolPatchXmlAsync(mainDol, tempXml, sourceRoot: null, cancellationToken))
                .EnsureDolPatchSuccess("No se pudo aplicar el parche XML.");
        }
        finally
        {
            if (File.Exists(tempXml))
            {
                File.Delete(tempXml);
            }
        }
    }

    private void CopyModFiles(BuildPlan plan, BuildOptions options)
    {
        var filesDir = Path.Combine(plan.WorkDirectory, "files");
        log("Copiando archivos del mod...");
        CopyDirectory(plan.ModDirectory, filesDir);

        if (!options.UseCustomBanner)
        {
            return;
        }

        var bannerId = plan.Mod.BannerId ?? plan.Mod.Id.ToLowerInvariant();
        var bnr = Path.Combine(paths.ResolveBannerDirectory(), $"{bannerId}.bnr");
        var arc = Path.Combine(paths.ResolveBannerDirectory(), $"{bannerId}.arc");
        if (!File.Exists(bnr) || !File.Exists(arc))
        {
            log("No hay banner personalizado para este mod; se conserva el original.");
            return;
        }

        File.Copy(bnr, Path.Combine(filesDir, "opening.bnr"), true);
        Directory.CreateDirectory(Path.Combine(filesDir, "ObjectData"));
        File.Copy(arc, Path.Combine(filesDir, "ObjectData", "SaveIconBanner.arc"), true);
    }

    private void CopyNativeFiles(NativeRiivolutionMod mod, string workDirectory)
    {
        var filesDir = Path.Combine(workDirectory, "files");
        foreach (var patch in mod.Plan.ActivePatches)
        {
            foreach (var folder in patch.Folders)
            {
                var source = ResolveExternalPath(mod.SourceRoot, patch.Root, folder.External, mod.Plan.Parameters);
                var destination = ResolveDiscPath(filesDir, folder.Disc, mod.Plan.Parameters);
                if (!Directory.Exists(source))
                {
                    log($"Carpeta no encontrada, se omite: {source}");
                    continue;
                }

                log($"Copiando carpeta Riivolution: {folder.External} -> {folder.Disc}");
                CopyNativeDirectory(source, destination, filesDir, folder);
            }

            foreach (var file in patch.Files)
            {
                var source = ResolveExternalPath(mod.SourceRoot, patch.Root, file.External, mod.Plan.Parameters);
                var destination = ResolveDiscPath(filesDir, file.Disc, mod.Plan.Parameters);
                if (!File.Exists(source))
                {
                    log($"Archivo no encontrado, se omite: {source}");
                    continue;
                }

                CopyNativeFile(source, destination, filesDir, file);
            }
        }
    }

    private async Task ApplyNativeDolPatchAsync(NativeBuildPlan plan, CancellationToken cancellationToken)
    {
        if (!plan.Mod.Plan.ActivePatches.SelectMany(patch => patch.MemoryPatches).Any())
        {
            return;
        }

        Directory.CreateDirectory(paths.TempDirectory);
        var patchXml = Path.Combine(paths.TempDirectory, $"{plan.OutputId}-riiv-dolpatch.xml");
        var mainDol = Path.Combine(plan.WorkDirectory, "sys", "main.dol");
        var document = RiivolutionPatchPlanner.CreateDolPatchXml(plan.Mod.Plan, mainDol);
        await File.WriteAllTextAsync(patchXml, document.ToString(SaveOptions.DisableFormatting), Encoding.UTF8, cancellationToken);

        log("Aplicando parches Riivolution con wit DOLPATCH...");
        (await toolchain.ApplyDolPatchXmlAsync(mainDol, patchXml, plan.Mod.SourceRoot, cancellationToken))
            .EnsureDolPatchSuccess("No se pudieron aplicar los parches Riivolution.");
    }

    private async Task ApplyGctPatchAsync(string mainDol, string patchFile, CancellationToken cancellationToken)
    {
        if (!File.Exists(patchFile))
        {
            throw new FileNotFoundException("No se encontro el parche GCT.", patchFile);
        }

        log("Aplicando parche GCT con wstrt...");
        (await toolchain.ApplyGctPatchAsync(mainDol, patchFile, cancellationToken))
            .EnsureSuccess("No se pudo aplicar el parche GCT.");
    }

    private void VerifyFreeSpace(GameDefinition game)
    {
        var root = Path.GetPathRoot(paths.RootDirectory)!;
        var required = game.RequiredFreeSpaceGb * 1024 * 1024 * 1024;
        var free = new DriveInfo(root).AvailableFreeSpace;
        if (free < required)
        {
            throw new InvalidOperationException($"Espacio insuficiente. Se requieren {game.RequiredFreeSpaceGb} GB libres.");
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        CopyDirectory(source, destination, createMissing: true);
    }

    private static void CopyDirectory(string source, string destination, bool createMissing)
    {
        if (!Directory.Exists(source))
        {
            throw new DirectoryNotFoundException($"No se encontro la carpeta descomprimida del mod: {source}");
        }

        if (createMissing)
        {
            Directory.CreateDirectory(destination);
        }
        else if (!Directory.Exists(destination))
        {
            return;
        }

        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            var targetDirectory = Path.Combine(destination, Path.GetRelativePath(source, directory));
            if (createMissing)
            {
                Directory.CreateDirectory(targetDirectory);
            }
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            if (!createMissing && !File.Exists(target))
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, true);
        }
    }

    private static void CopyNativeFile(string source, string destination, bool createMissing)
    {
        if (!createMissing && !File.Exists(destination))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, true);
    }

    private void CopyNativeFile(string source, string destination, string filesRoot, RiivolutionFileMapping file)
    {
        var targets = ResolveFileTargets(source, destination, filesRoot, file).ToList();
        if (targets.Count == 0)
        {
            log($"Destino no encontrado, se omite archivo Riivolution: {file.Disc}");
            return;
        }

        foreach (var target in targets)
        {
            CopyNativeFileWithPatchAttributes(source, target, file);
        }
    }

    private void CopyNativeDirectory(string source, string destination, string filesRoot, RiivolutionFolderMapping folder)
    {
        if (string.IsNullOrWhiteSpace(folder.Disc) || !folder.Disc.StartsWith('/') && !folder.Disc.StartsWith('\\'))
        {
            CopyNativeDirectoryByFilenameSearch(source, filesRoot, folder);
            return;
        }

        var targets = ResolveRootedFolderTargets(destination, folder).ToList();
        if (targets.Count == 0)
        {
            log($"Destino no encontrado, se omite carpeta Riivolution: {folder.Disc}");
            return;
        }

        foreach (var target in targets)
        {
            CopyNativeDirectoryWithPatchAttributes(source, target, folder);
        }
    }

    private static void CopyNativeDirectoryByFilenameSearch(string source, string filesRoot, RiivolutionFolderMapping folder)
    {
        var files = Directory.EnumerateFiles(
            source,
            "*",
            folder.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);

        foreach (var sourceFile in files)
        {
            foreach (var target in FindFilesByName(filesRoot, Path.GetFileName(sourceFile)))
            {
                var file = new RiivolutionFileMapping(sourceFile, target, folder.Resize, false, "", folder.Length);
                CopyNativeFileWithPatchAttributes(sourceFile, target, file);
            }
        }
    }

    private static void CopyNativeDirectoryWithPatchAttributes(string source, string destination, RiivolutionFolderMapping folder)
    {
        var files = Directory.EnumerateFiles(
            source,
            "*",
            folder.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);

        if (folder.Create)
        {
            Directory.CreateDirectory(destination);
        }
        else if (!Directory.Exists(destination))
        {
            return;
        }

        foreach (var sourceFile in files)
        {
            var relativePath = Path.GetRelativePath(source, sourceFile);
            var target = Path.Combine(destination, relativePath);
            if (!folder.Create && !File.Exists(target))
            {
                continue;
            }

            var file = new RiivolutionFileMapping(sourceFile, target, folder.Resize, folder.Create, "", folder.Length);
            CopyNativeFileWithPatchAttributes(sourceFile, target, file);
        }
    }

    private static void CopyNativeFileWithPatchAttributes(string source, string destination, RiivolutionFileMapping file)
    {
        var offset = ParseOptionalInteger(file.Offset);
        var length = ParseOptionalInteger(file.Length);
        var hasPartialPatch = offset is not null || length is not null || !file.Resize;

        if (!hasPartialPatch)
        {
            CopyNativeFile(source, destination, file.Create);
            return;
        }

        if (!File.Exists(destination))
        {
            if (!file.Create)
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using (var created = File.Create(destination))
            {
                if (offset is > 0)
                {
                    created.SetLength(offset.Value);
                }
            }
        }

        var sourceBytes = File.ReadAllBytes(source);
        var writeLength = length is > 0 ? Math.Min(length.Value, sourceBytes.Length) : sourceBytes.Length;
        using var stream = new FileStream(destination, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        stream.Position = offset ?? 0;
        if (!file.Resize && stream.Position + writeLength > stream.Length)
        {
            writeLength = (int)Math.Max(0, stream.Length - stream.Position);
        }

        stream.Write(sourceBytes, 0, writeLength);
        if (file.Resize && length is null && offset is null)
        {
            stream.SetLength(sourceBytes.Length);
        }
    }

    private static IEnumerable<string> ResolveFileTargets(string source, string destination, string filesRoot, RiivolutionFileMapping file)
    {
        if (string.IsNullOrWhiteSpace(file.Disc))
        {
            foreach (var match in FindFilesByName(filesRoot, Path.GetFileName(source)))
            {
                yield return match;
            }

            yield break;
        }

        if (!file.Disc.StartsWith('/') && !file.Disc.StartsWith('\\'))
        {
            foreach (var match in FindFilesByName(filesRoot, Path.GetFileName(file.Disc)))
            {
                yield return match;
            }

            yield break;
        }

        if (file.Create || File.Exists(destination))
        {
            yield return destination;
        }
    }

    private static IEnumerable<string> ResolveRootedFolderTargets(string destination, RiivolutionFolderMapping folder)
    {
        if (folder.Create || Directory.Exists(destination))
        {
            yield return destination;
        }
    }

    private static IEnumerable<string> FindFilesByName(string root, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || !Directory.Exists(root))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(root, fileName, SearchOption.AllDirectories))
        {
            if (Path.GetFileName(file).Equals(fileName, StringComparison.OrdinalIgnoreCase))
            {
                yield return file;
            }
        }
    }

    private static int? ParseOptionalInteger(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? Convert.ToInt32(value[2..], 16)
            : int.Parse(value);
    }

    private static string ResolveExternalPath(string sourceRoot, string patchRoot, string externalPath, IReadOnlyDictionary<string, string> parameters)
    {
        var resolvedExternal = RiivolutionPatchPlanner.ResolvePath(externalPath, parameters);
        if (externalPath.StartsWith('/') || externalPath.StartsWith('\\'))
        {
            return Path.Combine(sourceRoot, resolvedExternal);
        }

        return Path.Combine(sourceRoot, RiivolutionPatchPlanner.ResolvePath(patchRoot, parameters), resolvedExternal);
    }

    private static string ResolveDiscPath(string filesRoot, string discPath, IReadOnlyDictionary<string, string> parameters)
    {
        return Path.Combine(filesRoot, RiivolutionPatchPlanner.ResolvePath(discPath, parameters));
    }

    private static void DeleteIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
        }
    }

    private GameImage CreateGameImage(WitDisc disc)
    {
        var resolvedPath = ResolveWitPath(disc.Filename);
        var title = string.IsNullOrWhiteSpace(disc.Title) ? disc.Name : disc.Title;
        var game = catalog.CreateGame(disc.Id, title);
        var region = catalog.GetRegionByNameOrId(disc.Region, disc.Id);
        return new GameImage(resolvedPath, disc.Id, game, region);
    }

    private static IReadOnlyList<WitDisc> ParseWitList(string output)
    {
        var discs = new List<WitDisc>();
        var current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in output.SplitLines().Select(line => line.Trim()))
        {
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("[disc-", StringComparison.OrdinalIgnoreCase))
            {
                AddCurrent();
                current.Clear();
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            current[line[..separator]] = line[(separator + 1)..].Trim();
        }

        AddCurrent();
        return discs;

        void AddCurrent()
        {
            if (!current.TryGetValue("id", out var id) || id.Length == 0)
            {
                return;
            }

            var filename = current.TryGetValue("filename", out var file) ? file : "";
            if (filename.Length == 0)
            {
                filename = current.TryGetValue("source", out var source) ? source.Split("/#", StringSplitOptions.None)[0] : "";
            }

            if (filename.Length == 0)
            {
                return;
            }

            discs.Add(new WitDisc(
                id.ToUpperInvariant(),
                current.GetValueOrDefault("name", ""),
                current.GetValueOrDefault("title", ""),
                current.GetValueOrDefault("region", "").Trim(),
                filename));
        }
    }

    private string ResolveModArchive(ModDefinition mod)
    {
        var configured = Path.Combine(paths.ResolveRiivDirectory(), mod.Archive ?? $"{mod.Id}.zip");
        if (File.Exists(configured))
        {
            return configured;
        }

        foreach (var extension in new[] { ".zip", ".riiv" })
        {
            var fallback = Path.Combine(paths.ResolveRiivDirectory(), $"{mod.Id}{extension}");
            if (File.Exists(fallback))
            {
                return fallback;
            }
        }

        return configured;
    }

    private sealed record WitDisc(string Id, string Name, string Title, string Region, string Filename);

    private string ResolvePatchFile(ModDefinition mod, PatchKind patchKind, string outputId)
    {
        return patchKind switch
        {
            PatchKind.Gct => Path.Combine(paths.ResolveGctDirectory(), mod.PatchFile ?? $"{outputId}.gct"),
            PatchKind.Xml => Path.Combine(paths.ResolveXmlDirectory(), mod.PatchFile ?? $"{mod.Id}.xml"),
            _ => ""
        };
    }

    private static string ResolveNativeSourceRoot(string xmlFile)
    {
        var xmlDirectory = Path.GetDirectoryName(xmlFile)!;
        return Directory.GetParent(xmlDirectory)?.FullName ?? xmlDirectory;
    }

    private static string CreateChoiceSummary(RiivolutionDocument document, IReadOnlyList<int?> choiceIndexes)
    {
        var parts = new List<string>();
        var index = 0;
        foreach (var option in document.Sections.SelectMany(section => section.Options))
        {
            var choiceIndex = index < choiceIndexes.Count && choiceIndexes[index] is { } selected
                ? selected
                : option.DefaultChoice > 0 ? option.DefaultChoice - 1 : -1;
            index++;
            var optionName = string.IsNullOrWhiteSpace(option.Name) ? option.Id : option.Name;
            if (choiceIndex < 0)
            {
                if (!string.IsNullOrWhiteSpace(optionName))
                {
                    parts.Add($"{optionName}: Disabled");
                }

                continue;
            }

            if (choiceIndex >= option.Choices.Count)
            {
                continue;
            }

            var choiceName = option.Choices[choiceIndex].Name;
            if (!string.IsNullOrWhiteSpace(optionName) && !string.IsNullOrWhiteSpace(choiceName))
            {
                parts.Add($"{optionName}: {choiceName}");
            }
        }

        return string.Join("; ", parts);
    }

    private static void ValidateNativeDocumentForGame(RiivolutionDocument document, GameImage game)
    {
        var id = document.DiscId;
        if (id is null)
        {
            return;
        }

        if (!MatchesGame(id.Game, game.GameId))
        {
            throw new InvalidOperationException($"Este XML es para el juego '{id.Game}', pero la imagen seleccionada es '{game.GameId}'.");
        }

        if (!MatchesDeveloper(id.Developer, game.GameId))
        {
            throw new InvalidOperationException($"Este XML es para el maker '{id.Developer}', pero la imagen seleccionada es '{game.GameId}'.");
        }

        if (!MatchesRegion(id.Regions, game))
        {
            throw new InvalidOperationException($"Este XML no esta habilitado para la region '{game.Region.Name}' ({game.GameId}).");
        }
    }

    private static bool MatchesGame(string expected, string gameId)
    {
        if (string.IsNullOrWhiteSpace(expected))
        {
            return true;
        }

        var length = Math.Min(expected.Length, gameId.Length);
        return length > 0 && gameId.AsSpan(0, length).Equals(expected.AsSpan(0, length), StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesDeveloper(string expected, string gameId)
    {
        if (string.IsNullOrWhiteSpace(expected) || gameId.Length < 6)
        {
            return true;
        }

        return gameId[4..6].Equals(expected, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesRegion(IReadOnlyList<string> expectedRegions, GameImage game)
    {
        if (expectedRegions.Count == 0)
        {
            return true;
        }

        var regionCharacter = game.GameId.Length >= 4 ? game.GameId[3].ToString() : "";
        return expectedRegions.Any(region =>
            region.Equals(regionCharacter, StringComparison.OrdinalIgnoreCase)
            || region.Equals(game.Region.IdCharacter, StringComparison.OrdinalIgnoreCase)
            || region.Equals(game.Region.Code, StringComparison.OrdinalIgnoreCase)
            || region.Equals(game.Region.Name, StringComparison.OrdinalIgnoreCase));
    }

    private string ResolveWitPath(string path)
    {
        const string cygdrive = "/cygdrive/";
        if (path.StartsWith(cygdrive, StringComparison.OrdinalIgnoreCase) && path.Length > cygdrive.Length + 1)
        {
            var drive = char.ToUpperInvariant(path[cygdrive.Length]);
            var rest = path[(cygdrive.Length + 1)..].Replace('/', Path.DirectorySeparatorChar);
            return Path.GetFullPath($"{drive}:{Path.DirectorySeparatorChar}{rest.TrimStart(Path.DirectorySeparatorChar)}");
        }

        return Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(paths.RootDirectory, path));
    }
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


