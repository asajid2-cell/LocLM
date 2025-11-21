using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace LocLM.Services;

public record CommandResult(string StdOut, string StdErr, int ExitCode)
{
    public bool Success => ExitCode == 0;
}

public interface ICommandRunner
{
    Task<CommandResult> RunAsync(string command, string[]? args = null, string? workingDirectory = null, CancellationToken token = default);
}

public class WindowsCommandRunner : ICommandRunner
{
    public async Task<CommandResult> RunAsync(string command, string[]? args = null, string? workingDirectory = null, CancellationToken token = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c {command} {string.Join(" ", args ?? [])}",
            WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        return await RunProcessAsync(psi, token);
    }

    private static async Task<CommandResult> RunProcessAsync(ProcessStartInfo psi, CancellationToken token)
    {
        using var process = new Process { StartInfo = psi };
        process.Start();

        var stdout = await process.StandardOutput.ReadToEndAsync(token);
        var stderr = await process.StandardError.ReadToEndAsync(token);

        await process.WaitForExitAsync(token);

        return new CommandResult(stdout, stderr, process.ExitCode);
    }
}

public class UnixCommandRunner : ICommandRunner
{
    public async Task<CommandResult> RunAsync(string command, string[]? args = null, string? workingDirectory = null, CancellationToken token = default)
    {
        var fullCommand = args != null ? $"{command} {string.Join(" ", args)}" : command;

        var psi = new ProcessStartInfo
        {
            FileName = "/bin/bash",
            Arguments = $"-c \"{fullCommand.Replace("\"", "\\\"")}\"",
            WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        var stdout = await process.StandardOutput.ReadToEndAsync(token);
        var stderr = await process.StandardError.ReadToEndAsync(token);

        await process.WaitForExitAsync(token);

        return new CommandResult(stdout, stderr, process.ExitCode);
    }
}
