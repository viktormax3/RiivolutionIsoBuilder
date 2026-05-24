using System.IO.Compression;

namespace RiivolutionIsoBuilder;

public sealed class ArchiveExtractor
{
    private readonly Action<string> log;

    public ArchiveExtractor(Action<string> log)
    {
        this.log = log;
    }

    public Task ExtractAsync(string archive, string destination, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(destination);
        if (!archive.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            && !archive.EndsWith(".riiv", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Solo se soportan mods ZIP. Convierte el paquete Riivolution a .zip antes de usarlo.");
        }

        log("Extrayendo mod ZIP...");
        ZipFile.ExtractToDirectory(archive, destination, overwriteFiles: true);
        return Task.CompletedTask;
    }
}
