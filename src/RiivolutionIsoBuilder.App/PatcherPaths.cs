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
        OutputDirectory = Path.Combine(rootDirectory, "output");
        TempDirectory = Path.Combine(rootDirectory, "work");
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
    public string OutputDirectory { get; }
    public string TempDirectory { get; }

    public string Wit => Path.Combine(ToolsDirectory, "wit.exe");
    public string Wstrt => Path.Combine(ToolsDirectory, "wstrt.exe");
    public string TitlesFile => Path.Combine(ToolsDirectory, "titles.txt");
    public IEnumerable<string> GameSearchDirectories
    {
        get
        {
            if (Directory.Exists(GamesDirectory))
            {
                yield return GamesDirectory;
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

        throw new DirectoryNotFoundException("No se encontro data\\tools\\wit.exe ni una carpeta Base compatible.");
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
        return File.Exists(Path.Combine(path, "data", "tools", "wit.exe"))
            && File.Exists(Path.Combine(path, "data", "tools", "wstrt.exe"));
    }

    private static bool LooksLikeLegacyBase(string path)
    {
        return File.Exists(Path.Combine(path, "bin", "wit.exe"))
            && File.Exists(Path.Combine(path, "bin", "wstrt.exe"));
    }

    public string ResolveToolPath(string toolName)
    {
        if (!LegacyLayout)
        {
            return Path.Combine(ToolsDirectory, toolName);
        }

        return Path.Combine(RootDirectory, "bin", toolName);
    }

    public string ResolveRiivDirectory() => LegacyLayout ? Path.Combine(RootDirectory, "riiv_mods") : RiivDirectory;
    public string ResolveGctDirectory() => LegacyLayout ? Path.Combine(RootDirectory, "gct") : GctDirectory;
    public string ResolveXmlDirectory() => LegacyLayout ? Path.Combine(RootDirectory, "xml") : XmlDirectory;
    public string ResolveBannerDirectory() => LegacyLayout ? Path.Combine(RootDirectory, "banner") : BannerDirectory;
    public string ResolveCatalogFile() => LegacyLayout ? Path.Combine(RootDirectory, "catalog", "mods.json") : CatalogFile;
}

