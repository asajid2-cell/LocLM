using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace LocLM.Services;

public interface IFileSystemService
{
    string CurrentDirectory { get; }
    Task<List<FileSystemItem>> GetDirectoryContentsAsync(string path);
    Task<string> ReadFileAsync(string path);
    Task WriteFileAsync(string path, string content);
    Task CreateFileAsync(string path);
    Task CreateDirectoryAsync(string path);
    Task DeleteAsync(string path);
    Task RenameAsync(string oldPath, string newPath);
    bool IsDirectory(string path);
    bool Exists(string path);
    void SetCurrentDirectory(string path);
}

public class FileSystemItem
{
    public string Name { get; set; } = "";
    public string FullPath { get; set; } = "";
    public bool IsDirectory { get; set; }
    public bool IsExpanded { get; set; }
    public bool IsSelected { get; set; }
    public long Size { get; set; }
    public DateTime LastModified { get; set; }
    public List<FileSystemItem> Children { get; set; } = new();
    public int Depth { get; set; }

    public string Icon => IsDirectory
        ? (IsExpanded ? "▾" : "▸")
        : GetFileIcon(Name);

    public string SizeDisplay => IsDirectory ? "" : FormatSize(Size);

    private static string GetFileIcon(string name)
    {
        var ext = Path.GetExtension(name).ToLowerInvariant();
        return ext switch
        {
            ".cs" => "CS",
            ".py" => "PY",
            ".js" or ".jsx" => "JS",
            ".ts" or ".tsx" => "TS",
            ".json" => "{}",
            ".xml" or ".xaml" or ".axaml" => "<>",
            ".md" => "MD",
            ".txt" => "TXT",
            ".html" or ".htm" => "HT",
            ".css" or ".scss" or ".sass" => "CSS",
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".svg" or ".ico" => "IMG",
            ".sln" or ".csproj" => "NET",
            ".gitignore" or ".git" => "GIT",
            _ => "FILE"
        };
    }

    private static string FormatSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.#} {sizes[order]}";
    }
}

public class FileSystemService : IFileSystemService
{
    private string _currentDirectory;

    public string CurrentDirectory => _currentDirectory;

    public FileSystemService()
    {
        _currentDirectory = Environment.CurrentDirectory;
    }

    public void SetCurrentDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            _currentDirectory = path;
        }
    }

    public Task<List<FileSystemItem>> GetDirectoryContentsAsync(string path)
    {
        return Task.Run(() =>
        {
            var items = new List<FileSystemItem>();

            if (!Directory.Exists(path))
                return items;

            try
            {
                var dirs = Directory.GetDirectories(path)
                    .Where(d => !IsHidden(d))
                    .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase);

                foreach (var dir in dirs)
                {
                    var dirInfo = new DirectoryInfo(dir);
                    items.Add(new FileSystemItem
                    {
                        Name = dirInfo.Name,
                        FullPath = dir,
                        IsDirectory = true,
                        LastModified = dirInfo.LastWriteTime
                    });
                }

                var files = Directory.GetFiles(path)
                    .Where(f => !IsHidden(f))
                    .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase);

                foreach (var file in files)
                {
                    var fileInfo = new FileInfo(file);
                    items.Add(new FileSystemItem
                    {
                        Name = fileInfo.Name,
                        FullPath = file,
                        IsDirectory = false,
                        Size = fileInfo.Length,
                        LastModified = fileInfo.LastWriteTime
                    });
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Ignore directories we cannot access
            }

            return items;
        });
    }

    public async Task<string> ReadFileAsync(string path)
    {
        return await File.ReadAllTextAsync(path);
    }

    public async Task WriteFileAsync(string path, string content)
    {
        await File.WriteAllTextAsync(path, content);
    }

    public Task CreateFileAsync(string path)
    {
        File.Create(path).Dispose();
        return Task.CompletedTask;
    }

    public Task CreateDirectoryAsync(string path)
    {
        Directory.CreateDirectory(path);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, true);
        else if (File.Exists(path))
            File.Delete(path);
        return Task.CompletedTask;
    }

    public Task RenameAsync(string oldPath, string newPath)
    {
        if (Directory.Exists(oldPath))
            Directory.Move(oldPath, newPath);
        else if (File.Exists(oldPath))
            File.Move(oldPath, newPath);
        return Task.CompletedTask;
    }

    public bool IsDirectory(string path) => Directory.Exists(path);

    public bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    private static bool IsHidden(string path)
    {
        var name = Path.GetFileName(path);
        if (name.StartsWith('.') && name != ".github")
            return true;

        var excludedDirs = new[] { "node_modules", "bin", "obj", ".git", "__pycache__", ".vs", ".idea" };
        return excludedDirs.Contains(name, StringComparer.OrdinalIgnoreCase);
    }
}
