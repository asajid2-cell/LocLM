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
        ? (IsExpanded ? "▼" : "▶")
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

            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                return items;

            try
            {
                // Get directories with better error handling
                try
                {
                    var dirs = Directory.GetDirectories(path)
                        .Where(d => !IsHidden(d))
                        .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase);

                    foreach (var dir in dirs)
                    {
                        try
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
                        catch
                        {
                            // Skip individual directories that cause errors
                        }
                    }
                }
                catch
                {
                    // Failed to enumerate directories
                }

                // Get files with better error handling
                try
                {
                    var files = Directory.GetFiles(path)
                        .Where(f => !IsHidden(f))
                        .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase);

                    foreach (var file in files)
                    {
                        try
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
                        catch
                        {
                            // Skip individual files that cause errors
                        }
                    }
                }
                catch
                {
                    // Failed to enumerate files
                }
            }
            catch (Exception ex)
            {
                // Log but don't crash
                System.Diagnostics.Debug.WriteLine($"[FileSystem] Error reading directory {path}: {ex.Message}");
            }

            return items;
        });
    }

    public async Task<string> ReadFileAsync(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                System.Diagnostics.Debug.WriteLine($"[FileSystem] File not found: {path}");
                return string.Empty;
            }

            // Check file size before reading
            var fileInfo = new FileInfo(path);
            if (fileInfo.Length > 10 * 1024 * 1024) // 10 MB limit
            {
                System.Diagnostics.Debug.WriteLine($"[FileSystem] File too large: {path}");
                return "[File too large to display]";
            }

            return await File.ReadAllTextAsync(path);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FileSystem] Error reading file {path}: {ex.Message}");
            return $"[Error: {ex.Message}]";
        }
    }

    public async Task WriteFileAsync(string path, string content)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                System.Diagnostics.Debug.WriteLine("[FileSystem] Invalid path for write");
                return;
            }

            // Ensure directory exists
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(path, content ?? string.Empty);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FileSystem] Error writing file {path}: {ex.Message}");
            throw; // Re-throw for caller to handle
        }
    }

    public Task CreateFileAsync(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                System.Diagnostics.Debug.WriteLine("[FileSystem] Invalid path for create file");
                return Task.CompletedTask;
            }

            // Check if file already exists
            if (File.Exists(path))
            {
                System.Diagnostics.Debug.WriteLine($"[FileSystem] File already exists: {path}");
                return Task.CompletedTask;
            }

            // Ensure directory exists
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Create and immediately dispose to release file handle
            using (var stream = File.Create(path))
            {
                // File created, stream will be disposed automatically
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FileSystem] Error creating file {path}: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    public Task CreateDirectoryAsync(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                System.Diagnostics.Debug.WriteLine("[FileSystem] Invalid path for create directory");
                return Task.CompletedTask;
            }

            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FileSystem] Error creating directory {path}: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                System.Diagnostics.Debug.WriteLine("[FileSystem] Invalid path for delete");
                return Task.CompletedTask;
            }

            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
            else if (File.Exists(path))
            {
                // Check if file is read-only and remove attribute if needed
                var fileInfo = new FileInfo(path);
                if (fileInfo.IsReadOnly)
                {
                    fileInfo.IsReadOnly = false;
                }
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FileSystem] Error deleting {path}: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    public Task RenameAsync(string oldPath, string newPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(oldPath) || string.IsNullOrWhiteSpace(newPath))
            {
                System.Diagnostics.Debug.WriteLine("[FileSystem] Invalid paths for rename");
                return Task.CompletedTask;
            }

            // Check if target already exists
            if (File.Exists(newPath) || Directory.Exists(newPath))
            {
                System.Diagnostics.Debug.WriteLine($"[FileSystem] Target already exists: {newPath}");
                return Task.CompletedTask;
            }

            if (Directory.Exists(oldPath))
            {
                Directory.Move(oldPath, newPath);
            }
            else if (File.Exists(oldPath))
            {
                File.Move(oldPath, newPath);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FileSystem] Error renaming {oldPath} to {newPath}: {ex.Message}");
        }

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

