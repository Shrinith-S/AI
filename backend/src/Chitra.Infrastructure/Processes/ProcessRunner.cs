using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Chitra.Infrastructure.Processes;

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardErrorTail);

public sealed class ExecutableNotFoundException(string fileName, Exception inner)
    : Exception($"Executable '{fileName}' was not found.", inner);

/// <summary>Runs external tools (FFmpeg, Piper, nvidia-smi) with argument lists — never through a shell.</summary>
public static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null,
        string? workingDirectory = null,
        string? standardInput = null,
        Action<string>? onOutputLine = null)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = standardInput is not null,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        if (workingDirectory is not null) psi.WorkingDirectory = workingDirectory;
        foreach (var argument in arguments) psi.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var stdout = new StringBuilder();
        var stderrTail = new Queue<string>();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (stdout)
            {
                if (stdout.Length < 1_000_000) stdout.AppendLine(e.Data);
            }

            onOutputLine?.Invoke(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (stderrTail)
            {
                stderrTail.Enqueue(e.Data);
                while (stderrTail.Count > 40) stderrTail.Dequeue();
            }
        };

        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            throw new ExecutableNotFoundException(fileName, ex);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (standardInput is not null)
        {
            await process.StandardInput.WriteAsync(standardInput.AsMemory(), cancellationToken);
            process.StandardInput.Close();
        }

        using var timeoutCts = timeout is null ? new CancellationTokenSource() : new CancellationTokenSource(timeout.Value);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Already exited.
            }

            if (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"'{Path.GetFileName(fileName)}' did not finish within {timeout!.Value.TotalSeconds:0} seconds.");
            }

            throw;
        }

        // Make sure asynchronous output handlers have drained.
        process.WaitForExit();

        string tail;
        lock (stderrTail) tail = string.Join('\n', stderrTail);
        string output;
        lock (stdout) output = stdout.ToString();
        return new ProcessResult(process.ExitCode, output, tail);
    }
}
