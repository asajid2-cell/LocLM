using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LocLM.Services;

public interface IKeyboardService
{
    ObservableCollection<KeyboardShortcut> Shortcuts { get; }
    string CurrentVimMode { get; }
    bool IsVimEnabled { get; set; }
    event Action<string>? OnVimModeChanged;
    event Action<string>? OnShortcutTriggered;
    void SetVimMode(string mode);
    KeyboardShortcut? GetShortcut(string action);
    void UpdateShortcut(string action, string keys);
    void ResetToDefaults();
    void SaveShortcuts();
    void LoadShortcuts();
}

public partial class KeyboardShortcut : ObservableObject
{
    [ObservableProperty]
    private string _action = "";

    [ObservableProperty]
    private string _keys = "";

    [ObservableProperty]
    private string _description = "";

    [ObservableProperty]
    private string _category = "";

    [ObservableProperty]
    private bool _isEditing;

    public KeyboardShortcut() { }

    public KeyboardShortcut(string action, string keys, string description, string category)
    {
        Action = action;
        Keys = keys;
        Description = description;
        Category = category;
    }
}

public class KeyboardService : IKeyboardService
{
    private string _currentVimMode = "NORMAL";
    private bool _isVimEnabled = true;

    public ObservableCollection<KeyboardShortcut> Shortcuts { get; } = new();
    public string CurrentVimMode => _currentVimMode;
    public bool IsVimEnabled
    {
        get => _isVimEnabled;
        set => _isVimEnabled = value;
    }

    public event Action<string>? OnVimModeChanged;
    public event Action<string>? OnShortcutTriggered;

    public KeyboardService()
    {
        LoadShortcuts();
        if (Shortcuts.Count == 0)
            ResetToDefaults();
    }

    public void SetVimMode(string mode)
    {
        _currentVimMode = mode;
        OnVimModeChanged?.Invoke(mode);
    }

    public KeyboardShortcut? GetShortcut(string action)
    {
        foreach (var s in Shortcuts)
            if (s.Action == action) return s;
        return null;
    }

    public void UpdateShortcut(string action, string keys)
    {
        var shortcut = GetShortcut(action);
        if (shortcut != null)
        {
            shortcut.Keys = keys;
            SaveShortcuts();
        }
    }

    public void ResetToDefaults()
    {
        Shortcuts.Clear();

        // Global shortcuts
        AddShortcut("toggle_chat", "Ctrl+1", "Switch to Chat view", "Navigation");
        AddShortcut("toggle_editor", "Ctrl+2", "Switch to Editor view", "Navigation");
        AddShortcut("toggle_sidebar", "Ctrl+B", "Toggle file explorer", "Navigation");
        AddShortcut("toggle_settings", "Ctrl+,", "Open settings", "Navigation");
        AddShortcut("new_session", "Ctrl+N", "New chat session", "Navigation");
        AddShortcut("focus_input", "Ctrl+L", "Focus chat input", "Navigation");

        // File operations
        AddShortcut("save_file", "Ctrl+S", "Save current file", "File");
        AddShortcut("close_tab", "Ctrl+W", "Close current tab", "File");
        AddShortcut("close_all_tabs", "Ctrl+Shift+W", "Close all tabs", "File");
        AddShortcut("next_tab", "Ctrl+Tab", "Next tab", "File");
        AddShortcut("prev_tab", "Ctrl+Shift+Tab", "Previous tab", "File");

        // Editor (Vim Normal Mode)
        AddShortcut("vim_insert", "i", "Enter INSERT mode", "Vim Normal");
        AddShortcut("vim_insert_line_start", "I", "Insert at line start", "Vim Normal");
        AddShortcut("vim_append", "a", "Append after cursor", "Vim Normal");
        AddShortcut("vim_append_line_end", "A", "Append at line end", "Vim Normal");
        AddShortcut("vim_new_line_below", "o", "New line below", "Vim Normal");
        AddShortcut("vim_new_line_above", "O", "New line above", "Vim Normal");
        AddShortcut("vim_delete_line", "dd", "Delete line", "Vim Normal");
        AddShortcut("vim_yank_line", "yy", "Yank (copy) line", "Vim Normal");
        AddShortcut("vim_paste", "p", "Paste after cursor", "Vim Normal");
        AddShortcut("vim_undo", "u", "Undo", "Vim Normal");
        AddShortcut("vim_redo", "Ctrl+R", "Redo", "Vim Normal");
        AddShortcut("vim_goto_line_start", "0", "Go to line start", "Vim Normal");
        AddShortcut("vim_goto_line_end", "$", "Go to line end", "Vim Normal");
        AddShortcut("vim_goto_file_start", "gg", "Go to file start", "Vim Normal");
        AddShortcut("vim_goto_file_end", "G", "Go to file end", "Vim Normal");
        AddShortcut("vim_word_forward", "w", "Word forward", "Vim Normal");
        AddShortcut("vim_word_backward", "b", "Word backward", "Vim Normal");
        AddShortcut("vim_search", "/", "Search", "Vim Normal");

        // Vim modes
        AddShortcut("vim_normal", "Escape", "Exit to NORMAL mode", "Vim Modes");
        AddShortcut("vim_visual", "v", "Enter VISUAL mode", "Vim Modes");
        AddShortcut("vim_visual_line", "V", "Enter VISUAL LINE mode", "Vim Modes");
        AddShortcut("vim_command", ":", "Enter COMMAND mode", "Vim Modes");

        SaveShortcuts();
    }

    private void AddShortcut(string action, string keys, string description, string category)
    {
        Shortcuts.Add(new KeyboardShortcut(action, keys, description, category));
    }

    public void SaveShortcuts()
    {
        try
        {
            var path = GetConfigPath();
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var data = new List<ShortcutData>();
            foreach (var s in Shortcuts)
                data.Add(new ShortcutData { Action = s.Action, Keys = s.Keys });

            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save shortcuts: {ex.Message}");
        }
    }

    public void LoadShortcuts()
    {
        try
        {
            var path = GetConfigPath();
            if (!File.Exists(path)) return;

            var json = File.ReadAllText(path);
            var data = JsonSerializer.Deserialize<List<ShortcutData>>(json);
            if (data == null) return;

            // First load defaults to get descriptions
            ResetToDefaults();

            // Then override with saved keys
            foreach (var item in data)
            {
                var shortcut = GetShortcut(item.Action);
                if (shortcut != null)
                    shortcut.Keys = item.Keys;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load shortcuts: {ex.Message}");
        }
    }

    private static string GetConfigPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "LocLM", "keyboard-shortcuts.json");
    }

    private class ShortcutData
    {
        public string Action { get; set; } = "";
        public string Keys { get; set; } = "";
    }
}
