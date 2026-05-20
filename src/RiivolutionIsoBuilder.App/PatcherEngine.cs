using System.Text;
using System.Xml.Linq;
using RiivolutionIsoBuilder.Riivolution;

namespace RiivolutionIsoBuilder;

public sealed class PatcherEngine
{
    private readonly PatcherPaths paths;
    private readonly ModCatalog catalog;
    private readonly ExternalToolRunner runner;
    private readonly ArchiveExtractor extractor;
    private readonly Action<string> log;

    public PatcherEngine(PatcherPaths paths, Action<string> log)
    {
        this.paths = paths;
        this.log = log;
        catalog = ModCatalog.Load(paths.ResolveCatalogFile());
        runner = new ExternalToolRunner(paths, log);
        extractor = new ArchiveExtractor(log);
    }

    public async Task<GameImage?> InspectImageAsync(string imagePath, CancellationToken cancellationToken)
    {
        var result = await runner.RunAsync(paths.ResolveToolPath("wit.exe"), $"LIST --sections --titles {Quote(paths.TitlesFile)} {Quote(imagePath)}", cancellationToken);
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

            var result = await runner.RunAsync(
                paths.ResolveToolPath("wit.exe"),
                $"LIST -r {QuotePath(searchDirectory, trimTrailingSeparators: true)} --rdepth 5 --sections --titles {Quote(paths.TitlesFile)} -X {QuotePath(paths.ToolsDirectory, trimTrailingSeparators: true)}",
                cancellationToken);
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

    public NativeRiivolutionMod LoadNativeRiivolutionMod(string xmlFile, GameImage game)
    {
        var document = RiivolutionPatchReader.ReadDocument(xmlFile, game.Region.Name);
        var plan = RiivolutionPatchPlanner.CreateDefaultPlan(document, game.GameId);
        var sourceRoot = ResolveNativeSourceRoot(xmlFile, plan);
        return new NativeRiivolutionMod(xmlFile, sourceRoot, document, plan);
    }

    public string SuggestNativeOutputId(NativeRiivolutionMod mod, GameImage game)
    {
        var patchId = mod.Plan.ActivePatches.FirstOrDefault()?.Id ?? Path.GetFileNameWithoutExtension(mod.XmlFile);
        var prefix = new string(patchId.Where(char.IsLetterOrDigit).Take(3).ToArray()).ToUpperInvariant().PadRight(3, 'X');
        return $"{prefix}{game.GameId[3..6]}";
    }

    public BuildPlan CreatePlan(GameImage game, ModDefinition mod, BuildOptions options)
    {
        var suffix = game.GameId[3..6];
        var outputId = $"{mod.OutputIdPrefix ?? mod.Id}{suffix}";
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
        (await runner.RunAsync(paths.ResolveToolPath("wit.exe"), $"X {Quote(plan.Game.Path)} -PqD {Quote(plan.WorkDirectory)} --psel data", cancellationToken))
            .EnsureSuccess("No se pudo extraer la imagen del juego.");

        await ApplyPatchAsync(plan, cancellationToken);
        CopyModFiles(plan, options);

        log("Creando imagen modificada...");
        (await runner.RunAsync(paths.ResolveToolPath("wit.exe"), $"CP {Quote(plan.WorkDirectory)} -PqD {Quote(plan.OutputFile)}", cancellationToken))
            .EnsureSuccess("No se pudo crear la imagen modificada.");

        log("Editando ID, TMD y nombre interno...");
        (await runner.RunAsync(paths.ResolveToolPath("wit.exe"), $"ED --id {plan.OutputId} --name {Quote(plan.Mod.DisplayName)} --tt-id {plan.Tmd} {Quote(plan.OutputFile)}", cancellationToken))
            .EnsureSuccess("No se pudo editar la metadata de salida.");

        DeleteIfExists(paths.TempDirectory);
        log($"Listo: {plan.OutputFile}");
    }

    public NativeBuildPlan CreateNativePlan(GameImage game, NativeRiivolutionMod mod, string outputId, BuildOptions options)
    {
        outputId = NormalizeOutputId(outputId);
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
        (await runner.RunAsync(paths.ResolveToolPath("wit.exe"), $"X {Quote(plan.Game.Path)} -PqD {Quote(plan.WorkDirectory)} --psel data", cancellationToken))
            .EnsureSuccess("No se pudo extraer la imagen del juego.");

        CopyNativeFiles(plan.Mod, plan.WorkDirectory);
        await ApplyNativeDolPatchAsync(plan, cancellationToken);

        log("Creando imagen modificada...");
        (await runner.RunAsync(paths.ResolveToolPath("wit.exe"), $"CP {Quote(plan.WorkDirectory)} -PqD {Quote(plan.OutputFile)}", cancellationToken))
            .EnsureSuccess("No se pudo crear la imagen modificada.");

        log("Editando ID, TMD y nombre interno...");
        (await runner.RunAsync(paths.ResolveToolPath("wit.exe"), $"ED --id {plan.OutputId} --name {Quote(plan.InternalName)} --tt-id {plan.Tmd} {Quote(plan.OutputFile)}", cancellationToken))
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
            var patchFile = plan.PatchFile;
            if (!File.Exists(patchFile))
            {
                throw new FileNotFoundException("No se encontro el parche GCT.", patchFile);
            }

            log("Aplicando parche GCT con wstrt...");
            (await runner.RunAsync(paths.ResolveToolPath("wstrt.exe"), $"patch {Quote(mainDol)} --add-sect {Quote(patchFile)} -oPq", cancellationToken))
                .EnsureSuccess("No se pudo aplicar el parche GCT.");
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
            (await runner.RunAsync(paths.ResolveToolPath("wit.exe"), $"DOLPATCH {Quote(mainDol)} \"NEW=TEXT,0x80001800,1800\" \"XML={tempXml}\" -o", cancellationToken))
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
                var source = Path.Combine(mod.SourceRoot, RiivolutionPatchPlanner.ResolvePath(folder.External, mod.Plan.Parameters));
                var destination = Path.Combine(filesDir, RiivolutionPatchPlanner.ResolvePath(folder.Disc, mod.Plan.Parameters));
                if (!Directory.Exists(source))
                {
                    log($"Carpeta no encontrada, se omite: {source}");
                    continue;
                }

                log($"Copiando carpeta Riivolution: {folder.External} -> {folder.Disc}");
                CopyDirectory(source, destination);
            }

            foreach (var file in patch.Files)
            {
                var source = Path.Combine(mod.SourceRoot, RiivolutionPatchPlanner.ResolvePath(file.External, mod.Plan.Parameters));
                var destination = Path.Combine(filesDir, RiivolutionPatchPlanner.ResolvePath(file.Disc, mod.Plan.Parameters));
                if (!File.Exists(source))
                {
                    log($"Archivo no encontrado, se omite: {source}");
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination, true);
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
        (await runner.RunAsync(
            paths.ResolveToolPath("wit.exe"),
            $"DOLPATCH {Quote(mainDol)} \"NEW=TEXT,0x80001800,1800\" \"XML={patchXml}\" --source {Quote(plan.Mod.SourceRoot)} -o",
            cancellationToken))
            .EnsureDolPatchSuccess("No se pudieron aplicar los parches Riivolution.");
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
        if (!Directory.Exists(source))
        {
            throw new DirectoryNotFoundException($"No se encontro la carpeta descomprimida del mod: {source}");
        }

        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, true);
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
        }
    }

    private static string Quote(string value) => $"\"{value}\"";

    private static string QuotePath(string value, bool trimTrailingSeparators = false)
    {
        var normalized = Path.GetFullPath(value);
        if (trimTrailingSeparators)
        {
            var root = Path.GetPathRoot(normalized);
            while (normalized.Length > (root?.Length ?? 0) && (normalized.EndsWith('\\') || normalized.EndsWith('/')))
            {
                normalized = normalized[..^1];
            }
        }

        normalized = normalized.Replace('\\', '/');
        return Quote(normalized);
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

    private static string ResolveNativeSourceRoot(string xmlFile, RiivolutionPlan plan)
    {
        var xmlDirectory = Path.GetDirectoryName(xmlFile)!;
        var modRoot = Directory.GetParent(xmlDirectory)?.FullName ?? xmlDirectory;
        var patchRoot = plan.ActivePatches.FirstOrDefault()?.Root ?? "";
        return Path.Combine(modRoot, patchRoot.Trim('/', '\\').Replace('/', Path.DirectorySeparatorChar));
    }

    private static string NormalizeOutputId(string outputId)
    {
        outputId = new string(outputId.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        if (outputId.Length != 6)
        {
            throw new InvalidOperationException("El ID6 de salida debe tener exactamente 6 caracteres alfanumericos.");
        }

        return outputId;
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


