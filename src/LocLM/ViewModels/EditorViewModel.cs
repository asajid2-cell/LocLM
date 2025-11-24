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
    public ObservableCollection<EditorTab> OpenFiles => Tabs;

    [ObservableProperty]
    private bool _showLineNumbers = true;

    [ObservableProperty]
    private double _editorFontSize = 12;

    [RelayCommand]
    private void IncreaseFontSize()
    {
        if (EditorFontSize < 18)
            EditorFontSize += 1;
    }

    [RelayCommand]
    private void DecreaseFontSize()
    {
        if (EditorFontSize > 10)
            EditorFontSize -= 1;
    }

    [RelayCommand]
    private async Task OpenFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return;

        await OpenFileAsync(filePath);
    }

    public string CurrentFileContent
    {
        get => ActiveTab?.Content ?? string.Empty;
        set
        {
            if (ActiveTab == null)
                return;

            if (ActiveTab.Content != value)
            {
                ActiveTab.Content = value;
                // Don't call OnPropertyChanged here - it creates a loop
                // The ActiveTab.Content setter will handle notifications
            }
        }
    }

    public string LineNumbers => ActiveTab?.LineNumbers ?? "1";

    public EditorViewModel(IFileSystemService fileSystem)
    {
        _fileSystem = fileSystem;
        Tabs.CollectionChanged += (s, e) => HasOpenTabs = Tabs.Count > 0;
    }

    public async Task OpenFileAsync(string filePath)
    {
        var existing = Tabs.FirstOrDefault(t => t.FilePath == filePath);
        if (existing != null)
        {
            SetActiveTab(existing);
            return;
        }

        try
        {
            // Check file size before loading to prevent crashes
            var fileInfo = new FileInfo(filePath);
            const long maxFileSizeMB = 10; // 10 MB limit
            const long maxFileSize = maxFileSizeMB * 1024 * 1024;

            if (fileInfo.Length > maxFileSize)
            {
                System.Diagnostics.Debug.WriteLine($"File too large to open: {fileInfo.Length / 1024 / 1024} MB (max {maxFileSizeMB} MB)");
                // Could add a user-facing error message here
                return;
            }

            var content = await _fileSystem.ReadFileAsync(filePath);

            // Additional safety check for line count
            var lineCount = content.Split('\n').Length;
            var truncated = false;
            if (lineCount > 50000)
            {
                System.Diagnostics.Debug.WriteLine($"File has too many lines: {lineCount} (max 50000)");
                // Truncate to first 50000 lines
                var lines = content.Split('\n').Take(50000);
                content = string.Join("\n", lines) + "\n\n[... File truncated for performance ...]";
                truncated = true;
            }

            var tab = new EditorTab(filePath, content)
            {
                IsTruncated = truncated,
                LastWriteTimeUtc = fileInfo.LastWriteTimeUtc
            };
            Tabs.Add(tab);
            SetActiveTab(tab);
        }
        catch (Exception ex)
        {
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

        if (ActiveTab.IsTruncated)
        {
            System.Diagnostics.Debug.WriteLine("Save blocked: file was truncated when opened. Reload the full file before saving.");
            return;
        }

        // Detect external changes
        if (File.Exists(ActiveTab.FilePath))
        {
            var diskTime = File.GetLastWriteTimeUtc(ActiveTab.FilePath);
            if (diskTime > ActiveTab.LastWriteTimeUtc)
            {
                ActiveTab.IsStale = true;
                System.Diagnostics.Debug.WriteLine("Save blocked: file changed on disk. Reload before saving.");
                return;
            }
        }

        try
        {
            await _fileSystem.WriteFileAsync(ActiveTab.FilePath, ActiveTab.Content);
            ActiveTab.IsDirty = false;
            ActiveTab.OriginalContent = ActiveTab.Content;
            ActiveTab.LastWriteTimeUtc = File.GetLastWriteTimeUtc(ActiveTab.FilePath);
            ActiveTab.IsStale = false;
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

    partial void OnActiveTabChanged(EditorTab? value)
    {
        OnPropertyChanged(nameof(CurrentFileContent));
        OnPropertyChanged(nameof(LineNumbers));
    }
}

public partial class EditorTab : ObservableObject
{
    public string FilePath { get; }
    public string FileName { get; }
    public string FileExtension { get; }
    public string Language { get; }
    public bool IsTruncated { get; set; }
    public bool IsStale { get; set; }
    public DateTime LastWriteTimeUtc { get; set; }

    [ObservableProperty]
    private string _content;

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private bool _isDirty;

    public string OriginalContent { get; set; }

    public string Icon => GetFileIcon(FileName);
    public string Title
        => $"{(IsDirty ? "*" : string.Empty)}{FileName}{(IsTruncated ? " (truncated)" : string.Empty)}{(IsStale ? " (disk changed)" : string.Empty)}";

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
        // Don't regenerate line numbers on every keystroke - too expensive
        // Only update when explicitly requested
    }

    private static string GenerateLineNumbers(string content)
    {
        if (string.IsNullOrEmpty(content))
            return "1";

        // Limit line number generation to prevent performance issues
        var lines = content.Split('\n');
        var lineCount = Math.Min(lines.Length, 10000); // Cap at 10k lines for performance

        if (lineCount > 1000)
        {
            // For large files, just show count instead of all line numbers
            return $"1-{lineCount}";
        }

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
            ".cs" => "CS",
            ".py" => "PY",
            ".js" or ".ts" or ".jsx" or ".tsx" => "JS",
            ".json" => "JSON",
            ".xml" or ".xaml" or ".axaml" => "XML",
            ".md" => "MD",
            ".html" or ".htm" => "HTML",
            ".css" or ".scss" => "CSS",
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".svg" => "IMG",
            _ => "FILE"
        };
    }
}
