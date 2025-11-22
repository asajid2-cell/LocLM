using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace LocLM.Services;

public interface ITerminalService
{
    event Action<string>? OnOutput;
    event Action<string>? OnError;
    Task<CommandResult> ExecuteCommandAsync(string command, string? workingDirectory = null);
    void ClearOutput();
}

public class TerminalService : ITerminalService
{
    public event Action<string>? OnOutput;
    public event Action<string>? OnError;

    public async Task<CommandResult> ExecuteCommandAsync(string command, string? workingDirectory = null)
    {
        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        var processStartInfo = new ProcessStartInfo
        {
            FileName = GetShellExecutable(),
            Arguments = GetShellArguments(command),
            WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

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

        await process.WaitForExitAsync();

        return new CommandResult(
            StdOut: outputBuilder.ToString(),
            StdErr: errorBuilder.ToString(),
            ExitCode: process.ExitCode
        );
    }

    public void ClearOutput()
    {
        // This is handled by the UI clearing the terminal display
    }

    private static string GetShellExecutable()
    {
        if (OperatingSystem.IsWindows())
            return "powershell.exe";
        else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            return "/bin/bash";
        else
            return "cmd.exe";
    }

    private static string GetShellArguments(string command)
    {
        if (OperatingSystem.IsWindows())
            return $"-NoProfile -Command \"{command}\"";
        else
            return $"-c \"{command}\"";
    }
}
