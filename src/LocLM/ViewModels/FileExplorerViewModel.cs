using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocLM.Services;

namespace LocLM.ViewModels;

public partial class FileExplorerViewModel : ObservableObject
{
    private readonly IFileSystemService _fileSystem;

    [ObservableProperty]
    private string _rootPath = "";

    [ObservableProperty]
    private string _rootName = "";

    [ObservableProperty]
    private FileTreeItem? _selectedItem;

    [ObservableProperty]
    private string? _selectedFilePath;

    [ObservableProperty]
    private string? _selectedFileContent;

    [ObservableProperty]
    private bool _isLoading;

    public ObservableCollection<FileTreeItem> RootItems { get; } = new();

    public event Action<string, string>? OnFileOpened;

    public FileExplorerViewModel(IFileSystemService fileSystem)
    {
        _fileSystem = fileSystem;
    }

    public async Task LoadDirectoryAsync(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                System.Diagnostics.Debug.WriteLine($"[FileExplorer] Invalid path: {path}");
                // Fallback to current working directory if the requested path is missing
                var fallback = Directory.GetCurrentDirectory();
                if (!Directory.Exists(fallback))
                {
                    IsLoading = false;
                    return;
                }
                path = fallback;
            }

            IsLoading = true;
            RootPath = path;
            RootName = Path.GetFileName(path);
            if (string.IsNullOrEmpty(RootName))
                RootName = path; // For root drives like C:\

            RootItems.Clear();

            var root = new FileTreeItem(new FileSystemItem
            {
                Name = RootName,
                FullPath = path,
                IsDirectory = true
            }, _fileSystem, 0, this)
            {
                IsExpanded = true
            };

            await root.LoadChildrenAsync();

            // Only add if valid
            if (Directory.Exists(path))
            {
                RootItems.Add(root);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FileExplorer] Error loading directory {path}: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task RefreshCurrentDirectoryAsync()
    {
        if (!string.IsNullOrEmpty(RootPath))
        {
            await LoadDirectoryAsync(RootPath);
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await RefreshCurrentDirectoryAsync();
    }

    [RelayCommand]
    private async Task ToggleExpandAsync(FileTreeItem item)
    {
        if (!item.IsDirectory)
            return;

        if (item.IsExpanded)
        {
            item.IsExpanded = false;
            item.Children.Clear();
        }
        else
        {
            item.IsExpanded = true;
            await item.LoadChildrenAsync();
        }
    }

    public async Task SelectItemAsync(FileTreeItem item)
    {
        try
        {
            if (item == null)
            {
                System.Diagnostics.Debug.WriteLine("[FileExplorer] SelectItem called with null item");
                return;
            }

            // Deselect previous
            if (SelectedItem != null)
                SelectedItem.IsSelected = false;

            SelectedItem = item;
            item.IsSelected = true;
            SelectedFilePath = item.FullPath;

            if (item.IsDirectory)
            {
                // Toggle expand/collapse for directories
                await ToggleExpandAsync(item);
            }
            else
            {
                // Open file in editor - validate first
                if (string.IsNullOrWhiteSpace(item.FullPath) || !_fileSystem.Exists(item.FullPath))
                {
                    SelectedFileContent = "[File not found]";
                    System.Diagnostics.Debug.WriteLine($"[FileExplorer] File not found: {item.FullPath}");
                    return;
                }

                // Open file in editor
                try
                {
                    var content = await _fileSystem.ReadFileAsync(item.FullPath);
                    SelectedFileContent = content;
                    OnFileOpened?.Invoke(item.FullPath, content);
                }
                catch (Exception ex)
                {
                    SelectedFileContent = $"Error reading file: {ex.Message}";
                    System.Diagnostics.Debug.WriteLine($"[FileExplorer] Error opening file {item.FullPath}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FileExplorer] Error in SelectItemAsync: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task CreateFileAsync()
    {
        var parentPath = SelectedItem?.IsDirectory == true
            ? SelectedItem.FullPath
            : RootPath;

        var newFilePath = Path.Combine(parentPath, "untitled.txt");
        var counter = 1;
        while (File.Exists(newFilePath))
        {
            newFilePath = Path.Combine(parentPath, $"untitled{counter++}.txt");
        }

        await _fileSystem.CreateFileAsync(newFilePath);
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task CreateFolderAsync()
    {
        var parentPath = SelectedItem?.IsDirectory == true
            ? SelectedItem.FullPath
            : RootPath;

        var newFolderPath = Path.Combine(parentPath, "New Folder");
        var counter = 1;
        while (Directory.Exists(newFolderPath))
        {
            newFolderPath = Path.Combine(parentPath, $"New Folder {counter++}");
        }

        await _fileSystem.CreateDirectoryAsync(newFolderPath);
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task DeleteSelectedAsync()
    {
        if (SelectedItem == null)
            return;

        await _fileSystem.DeleteAsync(SelectedItem.FullPath);
        await RefreshAsync();
    }
}

public partial class FileTreeItem : ObservableObject
{
    private readonly IFileSystemService _fileSystem;
    private readonly FileExplorerViewModel _parent;

    public string Name { get; }
    public string FullPath { get; }
    public bool IsDirectory { get; }
    public string Icon => IsDirectory ? (IsExpanded ? "▾" : "▸") : "";
    public string FileIcon { get; }
    public int Depth { get; }
    public double IndentWidth => Depth * 16;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isSelected;

    public ObservableCollection<FileTreeItem> Children { get; } = new();

    public FileTreeItem(FileSystemItem item, IFileSystemService fileSystem, int depth, FileExplorerViewModel parent)
    {
        _fileSystem = fileSystem;
        _parent = parent;
        Name = item.Name;
        FullPath = item.FullPath;
        IsDirectory = item.IsDirectory;
        FileIcon = item.Icon;
        Depth = depth;
    }

    [RelayCommand]
    private async Task SelectAsync()
    {
        await _parent.SelectItemAsync(this);
    }

    public async Task LoadChildrenAsync()
    {
        if (!IsDirectory)
            return;

        Children.Clear();
        var items = await _fileSystem.GetDirectoryContentsAsync(FullPath);
        foreach (var item in items)
        {
            Children.Add(new FileTreeItem(item, _fileSystem, Depth + 1, _parent));
        }
    }

    partial void OnIsExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(Icon));
    }
}

