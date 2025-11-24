using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LocLM.Services;

public interface ITerminalService
{
    event Action<string>? OnOutput;
    event Action<string>? OnError;
    Task<CommandResult> ExecuteCommandAsync(string command, string? workingDirectory = null, int timeoutMs = 60000, CancellationToken cancellationToken = default);
    void ClearOutput();
}

public class TerminalService : ITerminalService
{
    public event Action<string>? OnOutput;
    public event Action<string>? OnError;

    public async Task<CommandResult> ExecuteCommandAsync(string command, string? workingDirectory = null, int timeoutMs = 60000, CancellationToken cancellationToken = default)
    {
        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        var (exe, args) = GetExecutableAndArgs(command);

        var processStartInfo = new ProcessStartInfo
        {
            FileName = exe,
            WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in args)
            processStartInfo.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = processStartInfo };

        process.OutputDataReceived += (s, e) =>
        {
            if (e.Data != null)
            {
                outputBuilder.AppendLine(e.Data);
                OnOutput?.Invoke(e.Data);
            }
        };

        process.ErrorDataReceived += (s, e) =>
        {
            if (e.Data != null)
            {
                errorBuilder.AppendLine(e.Data);
                OnError?.Invoke(e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var exited = await WaitForExitAsync(process, timeoutMs, cancellationToken);
        if (!exited && !process.HasExited)
        {
            try { process.Kill(true); } catch { }
        }

        return new CommandResult(
            StdOut: outputBuilder.ToString(),
            StdErr: errorBuilder.ToString(),
            ExitCode: process.HasExited ? process.ExitCode : -1
        );
    }

    public void ClearOutput()
    {
        // This is handled by the UI clearing the terminal display
    }

    private static (string exe, string[] args) GetExecutableAndArgs(string command)
    {
        if (OperatingSystem.IsWindows())
        {
            return ("powershell.exe", new[] { "-NoProfile", "-Command", $"& {{ {command} }}" });
        }
        else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            return ("/bin/bash", new[] { "-c", command });
        }
        else
        {
            return ("cmd.exe", new[] { "/c", command });
        }
    }

    private static async Task<bool> WaitForExitAsync(Process process, int timeoutMs, CancellationToken cancellationToken)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeoutMs);
            await process.WaitForExitAsync(cts.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
