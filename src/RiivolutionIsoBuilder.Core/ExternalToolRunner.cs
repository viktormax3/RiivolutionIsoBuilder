using System.Diagnostics;
using System.Text;

namespace RiivolutionIsoBuilder;

public sealed class ExternalToolRunner
{
    private readonly PatcherPaths paths;
    private readonly Action<string> log;

    public ExternalToolRunner(PatcherPaths paths, Action<string> log)
    {
        this.paths = paths;
        this.log = log;
    }

    public Task<ToolResult> RunAsync(string fileName, string arguments, CancellationToken cancellationToken)
    {
        return RunAsync(fileName, arguments, paths.RootDirectory, cancellationToken);
    }

    public async Task<ToolResult> RunAsync(string fileName, string arguments, string workingDirectory, CancellationToken cancellationToken)
    {
        if (!File.Exists(fileName))
        {
            throw new FileNotFoundException($"No se encontro la herramienta requerida: {Path.GetFileName(fileName)}. Revisa data/tools/<runtime> o RIIVOLUTION_ISO_BUILDER_TOOLS.", fileName);
        }

        log($"> {Path.GetFileName(fileName)} {arguments}");

        var output = new StringBuilder();
        var outputLock = new object();
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        process.OutputDataReceived += (_, e) => AppendLine(e.Data, output, outputLock);
        process.ErrorDataReceived += (_, e) => AppendLine(e.Data, output, outputLock);

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            await process.WaitForExitAsync(cancellationToken);
            process.WaitForExit();
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }

            throw;
        }

        lock (outputLock)
        {
            return new ToolResult(process.ExitCode, output.ToString());
        }
    }

    private void AppendLine(string? line, StringBuilder output, object outputLock)
    {
        if (line is null)
        {
            return;
        }

        lock (outputLock)
        {
            output.AppendLine(line);
        }

        if (!string.IsNullOrWhiteSpace(line))
        {
            log(line);
        }
    }
}

public sealed record ToolResult(int ExitCode, string Output)
{
    public void EnsureSuccess(string message)
    {
        if (ExitCode != 0)
        {
            throw new InvalidOperationException($"{message} Codigo: {ExitCode}");
        }
    }

    public void EnsureDolPatchSuccess(string message)
    {
        if (ExitCode == 0)
        {
            return;
        }

        // WIT DOLPATCH returns non-zero for normal Riivolution-style condition misses
        // ("original" did not match), even if useful patches were applied and saved.
        var saved = Output.Contains("Save patched DOL", StringComparison.OrdinalIgnoreCase)
            || Output.Contains("DOL not modified", StringComparison.OrdinalIgnoreCase);
        var hardError = Output.Contains("!Can't patch", StringComparison.OrdinalIgnoreCase)
            || Output.Contains("ERROR", StringComparison.OrdinalIgnoreCase)
            || Output.Contains("FATAL", StringComparison.OrdinalIgnoreCase);

        if (saved && !hardError)
        {
            return;
        }

        throw new InvalidOperationException($"{message} Codigo: {ExitCode}");
    }
}

