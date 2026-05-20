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
        log($"> {Path.GetFileName(fileName)} {arguments}");

        var output = new StringBuilder();
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
        process.OutputDataReceived += (_, e) => AppendLine(e.Data, output);
        process.ErrorDataReceived += (_, e) => AppendLine(e.Data, output);

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken);

        var text = output.ToString();
        if (!string.IsNullOrWhiteSpace(text))
        {
            log(text.TrimEnd());
        }

        return new ToolResult(process.ExitCode, text);
    }

    private void AppendLine(string? line, StringBuilder output)
    {
        if (line is null)
        {
            return;
        }

        output.AppendLine(line);
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

