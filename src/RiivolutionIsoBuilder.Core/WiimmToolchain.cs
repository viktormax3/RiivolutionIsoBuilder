namespace RiivolutionIsoBuilder;

public sealed class WiimmToolchain : IWiiToolchain
{
    private readonly PatcherPaths paths;
    private readonly ExternalToolRunner runner;

    public WiimmToolchain(PatcherPaths paths, Action<string> log)
    {
        this.paths = paths;
        runner = new ExternalToolRunner(paths, log);
    }

    public Task<ToolResult> InspectImageAsync(string imagePath, CancellationToken cancellationToken)
    {
        return runner.RunAsync(
            paths.ResolveToolPath("wit"),
            $"LIST --sections --titles {Quote(paths.TitlesFile)} {Quote(imagePath)}",
            cancellationToken);
    }

    public Task<ToolResult> ScanDirectoryAsync(string searchDirectory, CancellationToken cancellationToken)
    {
        return runner.RunAsync(
            paths.ResolveToolPath("wit"),
            $"LIST -r {QuotePath(searchDirectory, trimTrailingSeparators: true)} --rdepth 5 --sections --titles {Quote(paths.TitlesFile)} -X {QuotePath(paths.ResolveToolsDirectory(), trimTrailingSeparators: true)}",
            cancellationToken);
    }

    public Task<ToolResult> ExtractDataPartitionAsync(string imagePath, string workDirectory, CancellationToken cancellationToken)
    {
        return runner.RunAsync(
            paths.ResolveToolPath("wit"),
            $"X {Quote(imagePath)} -PqD {Quote(workDirectory)} --psel data",
            cancellationToken);
    }

    public Task<ToolResult> CreateImageAsync(string workDirectory, string outputFile, CancellationToken cancellationToken)
    {
        return runner.RunAsync(
            paths.ResolveToolPath("wit"),
            $"CP {Quote(workDirectory)} -PqD {Quote(outputFile)}",
            cancellationToken);
    }

    public Task<ToolResult> EditImageMetadataAsync(string imagePath, string outputId, string internalName, string tmdId, CancellationToken cancellationToken)
    {
        return runner.RunAsync(
            paths.ResolveToolPath("wit"),
            $"ED --id {outputId} --name {Quote(internalName)} --tt-id {tmdId} {Quote(imagePath)}",
            cancellationToken);
    }

    public Task<ToolResult> ApplyDolPatchXmlAsync(string mainDol, string patchXml, string? sourceRoot, CancellationToken cancellationToken)
    {
        var source = string.IsNullOrWhiteSpace(sourceRoot) ? "" : $" --source {Quote(sourceRoot)}";
        return runner.RunAsync(
            paths.ResolveToolPath("wit"),
            $"DOLPATCH {Quote(mainDol)} \"NEW=TEXT,0x80001800,1800\" \"XML={patchXml}\"{source} -o",
            cancellationToken);
    }

    public Task<ToolResult> ApplyGctPatchAsync(string mainDol, string patchFile, CancellationToken cancellationToken)
    {
        return runner.RunAsync(
            paths.ResolveToolPath("wstrt"),
            $"patch {Quote(mainDol)} --add-sect {Quote(patchFile)} -oPq",
            cancellationToken);
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
}
