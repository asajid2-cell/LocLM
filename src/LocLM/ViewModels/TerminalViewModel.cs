using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocLM.Services;

namespace LocLM.ViewModels;

public partial class TerminalViewModel : ObservableObject
{
    private readonly ITerminalService _terminal;

    [ObservableProperty]
    private string _name = "Terminal";

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private string _currentCommand = string.Empty;

    [ObservableProperty]
    private string _workingDirectory = string.Empty;

    [ObservableProperty]
    private bool _isExecuting;

    [ObservableProperty]
    private int _commandTimeoutMs = 60000;

    [ObservableProperty]
    private int _perCommandTimeoutMs = 60000;

    public ObservableCollection<TerminalLine> OutputLines { get; } = new();

    public TerminalViewModel(ITerminalService terminal)
    {
        _terminal = terminal;
        _workingDirectory = Environment.CurrentDirectory;

        _terminal.OnOutput += output =>
        {
            OutputLines.Add(new TerminalLine(output, TerminalLineType.Output));
        };

        _terminal.OnError += error =>
        {
            OutputLines.Add(new TerminalLine(error, TerminalLineType.Error));
        };
    }

    public void SetWorkingDirectory(string path)
    {
        if (System.IO.Directory.Exists(path))
        {
            WorkingDirectory = path;
            OutputLines.Add(new TerminalLine($"Working directory: {path}", TerminalLineType.Info));
        }
    }

    [RelayCommand]
    private async Task ExecuteCommandAsync()
    {
        if (string.IsNullOrWhiteSpace(CurrentCommand) || IsExecuting)
            return;

        var command = CurrentCommand;
        CurrentCommand = string.Empty;
        IsExecuting = true;

        // Add command to output
        OutputLines.Add(new TerminalLine($"> {command}", TerminalLineType.Command));

        try
        {
            var timeout = PerCommandTimeoutMs > 0 ? PerCommandTimeoutMs : CommandTimeoutMs;
            var result = await _terminal.ExecuteCommandAsync(command, WorkingDirectory, timeout);

            if (result.ExitCode != 0 && !string.IsNullOrEmpty(result.StdErr))
            {
                OutputLines.Add(new TerminalLine($"Exit code: {result.ExitCode}", TerminalLineType.Error));
            }
        }
        catch (Exception ex)
        {
            OutputLines.Add(new TerminalLine($"Error: {ex.Message}", TerminalLineType.Error));
        }
        finally
        {
            IsExecuting = false;
        }
    }

    [RelayCommand]
    private void Clear()
    {
        OutputLines.Clear();
        _terminal.ClearOutput();
    }
}

public record TerminalLine(string Text, TerminalLineType Type)
{
    public bool IsCommand => Type == TerminalLineType.Command;
    public bool IsOutput => Type == TerminalLineType.Output;
    public bool IsError => Type == TerminalLineType.Error;
    public bool IsInfo => Type == TerminalLineType.Info;
}

public enum TerminalLineType
{
    Command,
    Output,
    Error,
    Info
}
