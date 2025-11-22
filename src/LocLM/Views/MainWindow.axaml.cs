using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using LocLM.Services;
using LocLM.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace LocLM.Views;

public partial class MainWindow : Window
{
    private readonly IKeyboardService? _keyboardService;
    private string _keyBuffer = "";

    public MainWindow()
    {
        InitializeComponent();
        _keyboardService = App.Services?.GetService<IKeyboardService>();
        KeyDown += OnWindowKeyDown;
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        // Handle global shortcuts with modifiers first
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            switch (e.Key)
            {
                case Key.D1: // Ctrl+1: Switch to Chat
                    vm.SwitchToChatCommand.Execute(null);
                    e.Handled = true;
                    return;
                case Key.D2: // Ctrl+2: Switch to Editor
                    vm.SwitchToEditorCommand.Execute(null);
                    e.Handled = true;
                    return;
                case Key.B: // Ctrl+B: Toggle sidebar
                    vm.ToggleSidebarCommand.Execute(null);
                    e.Handled = true;
                    return;
                case Key.OemComma: // Ctrl+,: Toggle settings
                    vm.ToggleSettingsCommand.Execute(null);
                    e.Handled = true;
                    return;
                case Key.N: // Ctrl+N: New session
                    vm.NewSessionCommand.Execute(null);
                    e.Handled = true;
                    return;
                case Key.S: // Ctrl+S: Save file
                    if (vm.IsEditorView)
                    {
                        vm.Editor.SaveActiveTabCommand.Execute(null);
                        e.Handled = true;
                    }
                    return;
                case Key.W: // Ctrl+W: Close tab
                    if (vm.IsEditorView && vm.Editor.ActiveTab != null)
                    {
                        vm.Editor.CloseTabCommand.Execute(vm.Editor.ActiveTab);
                        e.Handled = true;
                    }
                    return;
                case Key.Tab: // Ctrl+Tab: Next tab
                    if (vm.IsEditorView)
                    {
                        vm.Editor.NextTabCommand.Execute(null);
                        e.Handled = true;
                    }
                    return;
                case Key.OemQuestion: // Ctrl+?: Toggle keyboard shortcuts
                    vm.ToggleKeyboardShortcutsCommand.Execute(null);
                    e.Handled = true;
                    return;
            }

            // Ctrl+Shift combinations
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                switch (e.Key)
                {
                    case Key.Tab: // Ctrl+Shift+Tab: Previous tab
                        if (vm.IsEditorView)
                        {
                            vm.Editor.PreviousTabCommand.Execute(null);
                            e.Handled = true;
                        }
                        return;
                    case Key.W: // Ctrl+Shift+W: Close all tabs
                        if (vm.IsEditorView)
                        {
                            vm.Editor.CloseAllTabsCommand.Execute(null);
                            e.Handled = true;
                        }
                        return;
                }
            }
        }

        // Escape key - close any open panels or exit vim modes
        if (e.Key == Key.Escape)
        {
            if (vm.IsKeyboardShortcutsOpen)
            {
                vm.IsKeyboardShortcutsOpen = false;
                e.Handled = true;
                return;
            }
            if (vm.IsSettingsOpen)
            {
                vm.IsSettingsOpen = false;
                e.Handled = true;
                return;
            }
            if (vm.IsModelSelectorOpen)
            {
                vm.IsModelSelectorOpen = false;
                e.Handled = true;
                return;
            }
            if (vm.IsSetupOpen)
            {
                vm.IsSetupOpen = false;
                e.Handled = true;
                return;
            }
            if (vm.IsFileMenuOpen || vm.IsEditMenuOpen || vm.IsViewMenuOpen || vm.IsRunMenuOpen)
            {
                vm.IsFileMenuOpen = false;
                vm.IsEditMenuOpen = false;
                vm.IsViewMenuOpen = false;
                vm.IsRunMenuOpen = false;
                e.Handled = true;
                return;
            }
            // Set vim to normal mode
            _keyboardService?.SetVimMode("NORMAL");
            _keyBuffer = "";
            e.Handled = true;
            return;
        }

        // Vim mode handling (only in editor view without modifiers)
        if (vm.IsEditorView && _keyboardService?.IsVimEnabled == true && e.KeyModifiers == KeyModifiers.None)
        {
            HandleVimKeys(e, vm);
        }
    }

    private void HandleVimKeys(KeyEventArgs e, MainWindowViewModel vm)
    {
        var mode = _keyboardService?.CurrentVimMode ?? "NORMAL";

        if (mode == "NORMAL")
        {
            // Build key buffer for multi-key commands like dd, yy, gg
            var keyChar = GetKeyChar(e.Key, e.KeyModifiers);
            if (!string.IsNullOrEmpty(keyChar))
            {
                _keyBuffer += keyChar;

                // Check for two-key commands
                if (_keyBuffer == "dd")
                {
                    // Delete line - would integrate with editor
                    _keyBuffer = "";
                    e.Handled = true;
                    return;
                }
                else if (_keyBuffer == "yy")
                {
                    // Yank line - would integrate with editor
                    _keyBuffer = "";
                    e.Handled = true;
                    return;
                }
                else if (_keyBuffer == "gg")
                {
                    // Go to file start - would integrate with editor
                    _keyBuffer = "";
                    e.Handled = true;
                    return;
                }

                // Single key commands
                if (_keyBuffer.Length == 1)
                {
                    switch (keyChar)
                    {
                        case "i": // Enter insert mode
                            _keyboardService?.SetVimMode("INSERT");
                            _keyBuffer = "";
                            e.Handled = true;
                            return;
                        case "I": // Insert at line start
                            _keyboardService?.SetVimMode("INSERT");
                            _keyBuffer = "";
                            e.Handled = true;
                            return;
                        case "a": // Append after cursor
                            _keyboardService?.SetVimMode("INSERT");
                            _keyBuffer = "";
                            e.Handled = true;
                            return;
                        case "A": // Append at line end
                            _keyboardService?.SetVimMode("INSERT");
                            _keyBuffer = "";
                            e.Handled = true;
                            return;
                        case "o": // New line below
                            _keyboardService?.SetVimMode("INSERT");
                            _keyBuffer = "";
                            e.Handled = true;
                            return;
                        case "O": // New line above
                            _keyboardService?.SetVimMode("INSERT");
                            _keyBuffer = "";
                            e.Handled = true;
                            return;
                        case "v": // Visual mode
                            _keyboardService?.SetVimMode("VISUAL");
                            _keyBuffer = "";
                            e.Handled = true;
                            return;
                        case "V": // Visual line mode
                            _keyboardService?.SetVimMode("VISUAL LINE");
                            _keyBuffer = "";
                            e.Handled = true;
                            return;
                        case ":": // Command mode
                            _keyboardService?.SetVimMode("COMMAND");
                            _keyBuffer = "";
                            e.Handled = true;
                            return;
                        case "G": // Go to end of file
                            _keyBuffer = "";
                            e.Handled = true;
                            return;
                        case "p": // Paste
                            _keyBuffer = "";
                            e.Handled = true;
                            return;
                        case "u": // Undo
                            _keyBuffer = "";
                            e.Handled = true;
                            return;
                    }
                }

                // Clear buffer if it's getting too long or doesn't match any command
                if (_keyBuffer.Length > 2)
                    _keyBuffer = "";
            }
        }
        else if (mode == "INSERT")
        {
            // In insert mode, let keys pass through to the editor
            // Only escape exits (handled above)
        }
    }

    private string GetKeyChar(Key key, KeyModifiers modifiers)
    {
        var shift = modifiers.HasFlag(KeyModifiers.Shift);
        return key switch
        {
            Key.A => shift ? "A" : "a",
            Key.B => shift ? "B" : "b",
            Key.C => shift ? "C" : "c",
            Key.D => shift ? "D" : "d",
            Key.E => shift ? "E" : "e",
            Key.F => shift ? "F" : "f",
            Key.G => shift ? "G" : "g",
            Key.H => shift ? "H" : "h",
            Key.I => shift ? "I" : "i",
            Key.J => shift ? "J" : "j",
            Key.K => shift ? "K" : "k",
            Key.L => shift ? "L" : "l",
            Key.M => shift ? "M" : "m",
            Key.N => shift ? "N" : "n",
            Key.O => shift ? "O" : "o",
            Key.P => shift ? "P" : "p",
            Key.Q => shift ? "Q" : "q",
            Key.R => shift ? "R" : "r",
            Key.S => shift ? "S" : "s",
            Key.T => shift ? "T" : "t",
            Key.U => shift ? "U" : "u",
            Key.V => shift ? "V" : "v",
            Key.W => shift ? "W" : "w",
            Key.X => shift ? "X" : "x",
            Key.Y => shift ? "Y" : "y",
            Key.Z => shift ? "Z" : "z",
            Key.OemSemicolon => shift ? ":" : ";",
            _ => ""
        };
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        // Enable window dragging from the top 38px (title bar height)
        if (e.GetPosition(this).Y <= 38)
        {
            BeginMoveDrag(e);
        }
    }

    private void CloseModelSelector_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Only close if clicking the backdrop, not the popup content
        if (e.Source == sender && DataContext is MainWindowViewModel vm)
        {
            vm.IsModelSelectorOpen = false;
        }
    }

    private void CloseSettings_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Only close if clicking the backdrop, not the popup content
        if (e.Source == sender && DataContext is MainWindowViewModel vm)
        {
            vm.IsSettingsOpen = false;
        }
    }

    private void CloseMenus_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Only close if clicking the backdrop, not the menu content
        if (e.Source == sender && DataContext is MainWindowViewModel vm)
        {
            vm.IsFileMenuOpen = false;
            vm.IsEditMenuOpen = false;
            vm.IsViewMenuOpen = false;
            vm.IsRunMenuOpen = false;
        }
    }

    private void InputTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        // Ctrl+Enter or just Enter (without Shift) sends the message
        if (e.Key == Key.Enter)
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                // Ctrl+Enter sends the message
                e.Handled = true;
                vm.SendMessageCommand.Execute(null);
            }
            else if (!e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                // Enter alone sends the message
                e.Handled = true;
                vm.SendMessageCommand.Execute(null);
            }
            // Shift+Enter allows new line (default TextBox behavior)
        }
    }
}
