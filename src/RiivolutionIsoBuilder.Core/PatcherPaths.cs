using System.Runtime.InteropServices;

namespace RiivolutionIsoBuilder;

public sealed class PatcherPaths
{
    public PatcherPaths(string rootDirectory)
    {
        RootDirectory = rootDirectory;
        DataDirectory = Path.Combine(rootDirectory, "data");
        ToolsDirectory = Path.Combine(DataDirectory, "tools");
        RiivDirectory = Path.Combine(DataDirectory, "mods");
        GctDirectory = Path.Combine(DataDirectory, "gct");
        XmlDirectory = Path.Combine(DataDirectory, "xml");
        BannerDirectory = Path.Combine(DataDirectory, "banner");
        CatalogFile = Path.Combine(DataDirectory, "catalog", "mods.json");
        GamesDirectory = Path.Combine(rootDirectory, "games");
        WorkspaceRiivolutionDirectory = Path.Combine(rootDirectory, "riivolution");
        WorkspaceGctDirectory = Path.Combine(rootDirectory, "gct");
        ImportDirectory = Path.Combine(rootDirectory, "imports");
        OutputDirectory = Path.Combine(rootDirectory, "output");
        TempDirectory = Path.Combine(rootDirectory, "work");
        EnsureWritableDirectories();
    }

    public string RootDirectory { get; }
    public string DataDirectory { get; }
    public string ToolsDirectory { get; }
    public string RiivDirectory { get; }
    public string GctDirectory { get; }
    public string XmlDirectory { get; }
    public string BannerDirectory { get; }
    public string CatalogFile { get; }
    public string GamesDirectory { get; }
    public string WorkspaceRiivolutionDirectory { get; }
    public string WorkspaceGctDirectory { get; }
    public string ImportDirectory { get; }
    public string OutputDirectory { get; }
    public string TempDirectory { get; }

    public string Wit => ResolveToolPath("wit");
    public string Wstrt => ResolveToolPath("wstrt");
    public string TitlesFile => ResolveTitlesFile();
    public bool UsesSharedAndroidWorkspace =>
        OperatingSystem.IsAndroid()
        && Environment.GetEnvironmentVariable("RIIVOLUTION_ISO_BUILDER_SHARED_WORKSPACE") == "1";

    public string? PublicAndroidWorkspace =>
        OperatingSystem.IsAndroid()
            ? Environment.GetEnvironmentVariable("RIIVOLUTION_ISO_BUILDER_PUBLIC_ROOT")
            : null;

    public IEnumerable<string> GameSearchDirectories
    {
        get
        {
            var importedGamesDirectory = Path.Combine(ImportDirectory, "games");
            if (Directory.Exists(GamesDirectory))
            {
                yield return GamesDirectory;
            }

            if (Directory.Exists(importedGamesDirectory))
            {
                yield return importedGamesDirectory;
            }

            if (OperatingSystem.IsAndroid())
            {
                yield break;
            }

            yield return RootDirectory;

            var legacyBase = Path.Combine(Path.GetDirectoryName(RootDirectory) ?? RootDirectory, "Base");
            if (Directory.Exists(legacyBase))
            {
                yield return legacyBase;
            }
        }
    }

    public static PatcherPaths Discover()
    {
        var overrideRoot = Environment.GetEnvironmentVariable("RIIVOLUTION_ISO_BUILDER_ROOT");
        if (!string.IsNullOrWhiteSpace(overrideRoot))
        {
            return new PatcherPaths(Path.GetFullPath(overrideRoot));
        }

        if (OperatingSystem.IsAndroid())
        {
            return new PatcherPaths(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RiivolutionIsoBuilder"));
        }

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (LooksLikeProjectRoot(dir.FullName))
            {
                return new PatcherPaths(dir.FullName);
            }

            var project = Path.Combine(dir.FullName, "RiivolutionIsoBuilder");
            if (LooksLikeProjectRoot(project))
            {
                return new PatcherPaths(project);
            }

            var legacyBase = Path.Combine(dir.FullName, "Base");
            if (LooksLikeLegacyBase(legacyBase))
            {
                return FromLegacyBase(legacyBase);
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("No se encontro una instalacion compatible de wit/wstrt. Revisa data/tools, data/tools/<runtime>, una carpeta Base compatible o RIIVOLUTION_ISO_BUILDER_TOOLS.");
    }

    public static PatcherPaths FromLegacyBase(string baseDirectory)
    {
        var paths = new PatcherPaths(baseDirectory)
        {
            LegacyLayout = true
        };
        return paths;
    }

    public bool LegacyLayout { get; private init; }

    private static bool LooksLikeProjectRoot(string path)
    {
        return Directory.Exists(Path.Combine(path, "data"))
            && FindCompatibleToolsDirectory(Path.Combine(path, "data", "tools")) is not null;
    }

    private static bool LooksLikeLegacyBase(string path)
    {
        return IsCompatibleToolsDirectory(Path.Combine(path, "bin"));
    }

    public string ResolveToolPath(string toolName)
    {
        var directory = ResolveToolsDirectory();
        foreach (var executableName in ResolveToolExecutableNames(toolName))
        {
            var candidate = Path.Combine(directory, executableName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(directory, ResolveToolExecutableNames(toolName).First());
    }

    public string ResolveToolsDirectory()
    {
        var overrideDirectory = Environment.GetEnvironmentVariable("RIIVOLUTION_ISO_BUILDER_TOOLS");
        if (!string.IsNullOrWhiteSpace(overrideDirectory) && IsCompatibleToolsDirectory(overrideDirectory))
        {
            return Path.GetFullPath(overrideDirectory);
        }

        if (LegacyLayout)
        {
            return Path.Combine(RootDirectory, "bin");
        }

        return FindCompatibleToolsDirectory(ToolsDirectory) ?? ToolsDirectory;
    }

    public string ResolveRiivDirectory() => LegacyLayout ? Path.Combine(RootDirectory, "riiv_mods") : RiivDirectory;
    public string ResolveGctDirectory() => LegacyLayout ? Path.Combine(RootDirectory, "gct") : GctDirectory;
    public string ResolveXmlDirectory() => LegacyLayout ? Path.Combine(RootDirectory, "xml") : XmlDirectory;
    public string ResolveBannerDirectory() => LegacyLayout ? Path.Combine(RootDirectory, "banner") : BannerDirectory;
    public string ResolveCatalogFile() => LegacyLayout ? Path.Combine(RootDirectory, "catalog", "mods.json") : CatalogFile;

    private string ResolveTitlesFile()
    {
        var toolchainTitles = Path.Combine(ResolveToolsDirectory(), "titles.txt");
        if (File.Exists(toolchainTitles))
        {
            return toolchainTitles;
        }

        return Path.Combine(ToolsDirectory, "titles.txt");
    }

    private static string? FindCompatibleToolsDirectory(string baseToolsDirectory)
    {
        foreach (var candidate in ToolDirectoryCandidates(baseToolsDirectory).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (IsCompatibleToolsDirectory(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> ToolDirectoryCandidates(string baseToolsDirectory)
    {
        foreach (var name in PlatformToolDirectoryNames())
        {
            yield return Path.Combine(baseToolsDirectory, name);
        }

        yield return baseToolsDirectory;
    }

    private static bool IsCompatibleToolsDirectory(string directory)
    {
        return ToolExists(directory, "wit")
            && ToolExists(directory, "wstrt");
    }

    private static bool ToolExists(string directory, string toolName)
    {
        return ResolveToolExecutableNames(toolName)
            .Any(executableName => File.Exists(Path.Combine(directory, executableName)));
    }

    private static IEnumerable<string> PlatformToolDirectoryNames()
    {
        var os = OperatingSystem.IsWindows() ? "win"
            : OperatingSystem.IsLinux() ? "linux"
            : OperatingSystem.IsMacOS() ? "osx"
            : OperatingSystem.IsAndroid() ? "android"
            : "unknown";
        var arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.X86 => "x86",
            Architecture.Arm64 => "arm64",
            Architecture.Arm => "arm",
            _ => RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()
        };

        yield return $"{os}-{arch}";
        yield return os;
    }

    private static IEnumerable<string> ResolveToolExecutableNames(string toolName)
    {
        if (Path.GetExtension(toolName).Length > 0)
        {
            yield return toolName;
            yield break;
        }

        if (OperatingSystem.IsWindows())
        {
            yield return $"{toolName}.exe";
            yield break;
        }

        if (OperatingSystem.IsAndroid())
        {
            yield return $"lib{toolName}.so";
        }

        yield return toolName;
    }

    private void EnsureWritableDirectories()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(ToolsDirectory);
        Directory.CreateDirectory(RiivDirectory);
        Directory.CreateDirectory(GctDirectory);
        Directory.CreateDirectory(XmlDirectory);
        Directory.CreateDirectory(BannerDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(CatalogFile)!);
        Directory.CreateDirectory(GamesDirectory);
        Directory.CreateDirectory(WorkspaceRiivolutionDirectory);
        Directory.CreateDirectory(WorkspaceGctDirectory);
        Directory.CreateDirectory(ImportDirectory);
        Directory.CreateDirectory(Path.Combine(ImportDirectory, "games"));
        Directory.CreateDirectory(Path.Combine(ImportDirectory, "xml"));
        Directory.CreateDirectory(Path.Combine(ImportDirectory, "gct"));
        Directory.CreateDirectory(OutputDirectory);
        Directory.CreateDirectory(TempDirectory);
    }
}
