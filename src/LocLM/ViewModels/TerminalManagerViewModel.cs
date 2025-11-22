using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocLM.Services;

namespace LocLM.ViewModels;

public partial class TerminalManagerViewModel : ObservableObject
{
    private readonly ITerminalService _terminalService;
    private int _terminalCounter = 1;

    [ObservableProperty]
    private TerminalViewModel? _activeTerminal;

    [ObservableProperty]
    private bool _hasTerminals;

    public ObservableCollection<TerminalViewModel> Terminals { get; } = new();

    public TerminalManagerViewModel(ITerminalService terminalService)
    {
        _terminalService = terminalService;

        // Create initial terminal
        CreateNewTerminal();

        Terminals.CollectionChanged += (s, e) => HasTerminals = Terminals.Count > 0;
    }

    [RelayCommand]
    private void CreateNewTerminal()
    {
        var terminal = new TerminalViewModel(_terminalService)
        {
            Name = $"Terminal {_terminalCounter++}"
        };

        Terminals.Add(terminal);
        SetActiveTerminal(terminal);
    }

    [RelayCommand]
    private void SetActiveTerminal(TerminalViewModel terminal)
    {
        foreach (var t in Terminals)
            t.IsActive = false;

        terminal.IsActive = true;
        ActiveTerminal = terminal;
    }

    [RelayCommand]
    private void CloseTerminal(TerminalViewModel terminal)
    {
        var index = Terminals.IndexOf(terminal);
        Terminals.Remove(terminal);

        if (Terminals.Count > 0)
        {
            var newIndex = System.Math.Min(index, Terminals.Count - 1);
            SetActiveTerminal(Terminals[newIndex]);
        }
        else
        {
            // Always maintain at least one terminal
            CreateNewTerminal();
        }
    }

    [RelayCommand]
    private void CloseAllTerminals()
    {
        Terminals.Clear();
        _terminalCounter = 1;
        CreateNewTerminal();
    }

    [RelayCommand]
    private void NextTerminal()
    {
        if (ActiveTerminal == null || Terminals.Count <= 1)
            return;

        var currentIndex = Terminals.IndexOf(ActiveTerminal);
        var nextIndex = (currentIndex + 1) % Terminals.Count;
        SetActiveTerminal(Terminals[nextIndex]);
    }

    [RelayCommand]
    private void PreviousTerminal()
    {
        if (ActiveTerminal == null || Terminals.Count <= 1)
            return;

        var currentIndex = Terminals.IndexOf(ActiveTerminal);
        var prevIndex = currentIndex == 0 ? Terminals.Count - 1 : currentIndex - 1;
        SetActiveTerminal(Terminals[prevIndex]);
    }

    [RelayCommand]
    private void RenameTerminal(TerminalViewModel terminal)
    {
        // This would trigger a rename dialog in the UI
        // For now, we'll just cycle through some preset names
        var names = new[] { "Terminal", "PowerShell", "Bash", "Command", "Shell" };
        var currentName = terminal.Name.Split(' ')[0];
        var nextName = names[(System.Array.IndexOf(names, currentName) + 1) % names.Length];
        terminal.Name = $"{nextName} {terminal.Name.Split(' ').Last()}";
    }

    public void SetWorkingDirectory(string path)
    {
        if (ActiveTerminal != null)
        {
            ActiveTerminal.SetWorkingDirectory(path);
        }
    }
}
