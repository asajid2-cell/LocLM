using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocLM.Services;

namespace LocLM.ViewModels;

public partial class EditorViewModel : ObservableObject
{
    private readonly IFileSystemService _fileSystem;

    [ObservableProperty]
    private EditorTab? _activeTab;

    [ObservableProperty]
    private bool _hasOpenTabs;

    public ObservableCollection<EditorTab> Tabs { get; } = new();

    public EditorViewModel(IFileSystemService fileSystem)
    {
        _fileSystem = fileSystem;
        Tabs.CollectionChanged += (s, e) => HasOpenTabs = Tabs.Count > 0;
    }

    public async Task OpenFileAsync(string filePath)
    {
        // Check if already open
        var existing = Tabs.FirstOrDefault(t => t.FilePath == filePath);
        if (existing != null)
        {
            SetActiveTab(existing);
            return;
        }

        try
        {
            var content = await _fileSystem.ReadFileAsync(filePath);
            var tab = new EditorTab(filePath, content);
            Tabs.Add(tab);
            SetActiveTab(tab);
        }
        catch (Exception ex)
        {
            // Could show error in UI
            System.Diagnostics.Debug.WriteLine($"Failed to open file: {ex.Message}");
        }
    }

    [RelayCommand]
    private void SetActiveTab(EditorTab tab)
    {
        foreach (var t in Tabs)
            t.IsActive = false;

        tab.IsActive = true;
        ActiveTab = tab;
    }

    [RelayCommand]
    private void CloseTab(EditorTab tab)
    {
        var index = Tabs.IndexOf(tab);
        Tabs.Remove(tab);

        // Select adjacent tab
        if (Tabs.Count > 0)
        {
            var newIndex = Math.Min(index, Tabs.Count - 1);
            SetActiveTab(Tabs[newIndex]);
        }
        else
        {
            ActiveTab = null;
        }
    }

    [RelayCommand]
    private void CloseAllTabs()
    {
        Tabs.Clear();
        ActiveTab = null;
    }

    [RelayCommand]
    private void CloseOtherTabs(EditorTab keepTab)
    {
        var toRemove = Tabs.Where(t => t != keepTab).ToList();
        foreach (var tab in toRemove)
            Tabs.Remove(tab);

        SetActiveTab(keepTab);
    }

    [RelayCommand]
    private async Task SaveActiveTabAsync()
    {
        if (ActiveTab == null || !ActiveTab.IsDirty)
            return;

        try
        {
            await _fileSystem.WriteFileAsync(ActiveTab.FilePath, ActiveTab.Content);
            ActiveTab.IsDirty = false;
            ActiveTab.OriginalContent = ActiveTab.Content;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save file: {ex.Message}");
        }
    }

    [RelayCommand]
    private void NextTab()
    {
        if (ActiveTab == null || Tabs.Count <= 1)
            return;

        var currentIndex = Tabs.IndexOf(ActiveTab);
        var nextIndex = (currentIndex + 1) % Tabs.Count;
        SetActiveTab(Tabs[nextIndex]);
    }

    [RelayCommand]
    private void PreviousTab()
    {
        if (ActiveTab == null || Tabs.Count <= 1)
            return;

        var currentIndex = Tabs.IndexOf(ActiveTab);
        var prevIndex = currentIndex == 0 ? Tabs.Count - 1 : currentIndex - 1;
        SetActiveTab(Tabs[prevIndex]);
    }
}

public partial class EditorTab : ObservableObject
{
    public string FilePath { get; }
    public string FileName { get; }
    public string FileExtension { get; }
    public string Language { get; }

    [ObservableProperty]
    private string _content;

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private bool _isDirty;

    public string OriginalContent { get; set; }

    public string Icon => GetFileIcon(FileName);
    public string Title => IsDirty ? $"{FileName} •" : FileName;

    // Line numbers for display
    public string LineNumbers => GenerateLineNumbers(Content);

    public EditorTab(string filePath, string content)
    {
        FilePath = filePath;
        FileName = Path.GetFileName(filePath);
        FileExtension = Path.GetExtension(filePath).ToLowerInvariant();
        Language = GetLanguage(FileExtension);
        Content = content;
        OriginalContent = content;
    }

    partial void OnContentChanged(string value)
    {
        IsDirty = value != OriginalContent;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(LineNumbers));
    }

    private static string GenerateLineNumbers(string content)
    {
        if (string.IsNullOrEmpty(content))
            return "1";

        var lineCount = content.Split('\n').Length;
        return string.Join("\n", Enumerable.Range(1, lineCount));
    }

    private static string GetLanguage(string ext) => ext switch
    {
        ".cs" => "csharp",
        ".py" => "python",
        ".js" => "javascript",
        ".ts" => "typescript",
        ".jsx" or ".tsx" => "react",
        ".json" => "json",
        ".xml" or ".xaml" or ".axaml" => "xml",
        ".html" or ".htm" => "html",
        ".css" or ".scss" => "css",
        ".md" => "markdown",
        ".yaml" or ".yml" => "yaml",
        ".sh" or ".bash" => "bash",
        ".sql" => "sql",
        _ => "plaintext"
    };

    private static string GetFileIcon(string name)
    {
        var ext = Path.GetExtension(name).ToLowerInvariant();
        return ext switch
        {
            ".cs" => "🔷",
            ".py" => "🐍",
            ".js" or ".ts" or ".jsx" or ".tsx" => "📜",
            ".json" => "{ }",
            ".xml" or ".xaml" or ".axaml" => "📋",
            ".md" => "📝",
            ".html" or ".htm" => "🌐",
            ".css" or ".scss" => "🎨",
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".svg" => "🖼️",
            _ => "📄"
        };
    }
}
