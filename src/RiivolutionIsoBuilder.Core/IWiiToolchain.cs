namespace RiivolutionIsoBuilder;

public interface IWiiToolchain
{
    Task<ToolResult> InspectImageAsync(string imagePath, CancellationToken cancellationToken);
    Task<ToolResult> ScanDirectoryAsync(string searchDirectory, CancellationToken cancellationToken);
    Task<ToolResult> ExtractDataPartitionAsync(string imagePath, string workDirectory, CancellationToken cancellationToken);
    Task<ToolResult> CreateImageAsync(string workDirectory, string outputFile, CancellationToken cancellationToken);
    Task<ToolResult> EditImageMetadataAsync(string imagePath, string outputId, string internalName, string tmdId, CancellationToken cancellationToken);
    Task<ToolResult> ApplyDolPatchXmlAsync(string mainDol, string patchXml, string? sourceRoot, CancellationToken cancellationToken);
    Task<ToolResult> ApplyGctPatchAsync(string mainDol, string patchFile, CancellationToken cancellationToken);
}
