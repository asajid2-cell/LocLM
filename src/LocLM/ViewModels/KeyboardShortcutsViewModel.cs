using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocLM.Services;

namespace LocLM.ViewModels;

public partial class KeyboardShortcutsViewModel : ObservableObject
{
    private readonly IKeyboardService _keyboardService;

    [ObservableProperty]
    private string _vimMode = "NORMAL";

    [ObservableProperty]
    private bool _isVimEnabled = true;

    [ObservableProperty]
    private string _selectedCategory = "All";

    [ObservableProperty]
    private KeyboardShortcut? _editingShortcut;

    [ObservableProperty]
    private string _newKeyBinding = "";

    public ObservableCollection<KeyboardShortcut> AllShortcuts => _keyboardService.Shortcuts;

    public ObservableCollection<KeyboardShortcut> FilteredShortcuts { get; } = new();

    public ObservableCollection<string> Categories { get; } = new()
    {
        "All", "Navigation", "File", "Vim Normal", "Vim Modes"
    };

    public KeyboardShortcutsViewModel(IKeyboardService keyboardService)
    {
        _keyboardService = keyboardService;
        IsVimEnabled = keyboardService.IsVimEnabled;
        VimMode = keyboardService.CurrentVimMode;

        _keyboardService.OnVimModeChanged += mode => VimMode = mode;

        FilterShortcuts();
    }

    partial void OnSelectedCategoryChanged(string value)
    {
        FilterShortcuts();
    }

    partial void OnIsVimEnabledChanged(bool value)
    {
        _keyboardService.IsVimEnabled = value;
    }

    private void FilterShortcuts()
    {
        FilteredShortcuts.Clear();
        foreach (var shortcut in AllShortcuts)
        {
            if (SelectedCategory == "All" || shortcut.Category == SelectedCategory)
                FilteredShortcuts.Add(shortcut);
        }
    }

    [RelayCommand]
    private void StartEditing(KeyboardShortcut shortcut)
    {
        // Stop editing any other shortcut
        foreach (var s in AllShortcuts)
            s.IsEditing = false;

        shortcut.IsEditing = true;
        EditingShortcut = shortcut;
        NewKeyBinding = shortcut.Keys;
    }

    [RelayCommand]
    private void SaveEditing()
    {
        if (EditingShortcut != null && !string.IsNullOrEmpty(NewKeyBinding))
        {
            _keyboardService.UpdateShortcut(EditingShortcut.Action, NewKeyBinding);
            EditingShortcut.IsEditing = false;
            EditingShortcut = null;
            NewKeyBinding = "";
        }
    }

    [RelayCommand]
    private void CancelEditing()
    {
        if (EditingShortcut != null)
        {
            EditingShortcut.IsEditing = false;
            EditingShortcut = null;
            NewKeyBinding = "";
        }
    }

    [RelayCommand]
    private void ResetToDefaults()
    {
        _keyboardService.ResetToDefaults();
        FilterShortcuts();
    }

    public void SetVimMode(string mode)
    {
        _keyboardService.SetVimMode(mode);
    }
}
