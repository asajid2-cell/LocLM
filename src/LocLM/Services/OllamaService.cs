using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace LocLM.Services;

public class OllamaService : IOllamaService
{
    private readonly HttpClient _client;
    private const string DefaultBaseUrl = "http://localhost:11434";
    private OllamaStatus _currentStatus = OllamaStatus.NotInstalled;

    public event Action<OllamaStatus>? OnStatusChanged;

    public OllamaService()
    {
        _client = new HttpClient
        {
            BaseAddress = new Uri(DefaultBaseUrl),
            Timeout = TimeSpan.FromSeconds(10)
        };
    }

    public async Task<bool> IsInstalledAsync(CancellationToken token = default)
    {
        try
        {
            // First check if Ollama server is already running (most reliable)
            if (await IsRunningAsync(token))
            {
                UpdateStatus(OllamaStatus.Running);
                return true;
            }

            // Then check if binary exists
            var version = await GetVersionAsync(token);
            var installed = !string.IsNullOrEmpty(version);
            UpdateStatus(installed ? OllamaStatus.Installed : OllamaStatus.NotInstalled);
            return installed;
        }
        catch
        {
            // Last resort: check if server is running
            if (await IsRunningAsync(token))
            {
                UpdateStatus(OllamaStatus.Running);
                return true;
            }
            UpdateStatus(OllamaStatus.NotInstalled);
            return false;
        }
    }

    public async Task<bool> IsRunningAsync(CancellationToken token = default)
    {
        try
        {
            var response = await _client.GetAsync("/api/tags", token);
            var running = response.IsSuccessStatusCode;
            if (running) UpdateStatus(OllamaStatus.Running);
            return running;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> StartServerAsync(CancellationToken token = default)
    {
        if (await IsRunningAsync(token))
            return true;

        UpdateStatus(OllamaStatus.Starting);

        try
        {
            var ollamaPath = GetOllamaPath();
            if (string.IsNullOrEmpty(ollamaPath))
            {
                UpdateStatus(OllamaStatus.NotInstalled);
                return false;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = ollamaPath,
                Arguments = "serve",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            var process = Process.Start(startInfo);
            if (process == null)
            {
                UpdateStatus(OllamaStatus.Error);
                return false;
            }

            // Wait for server to start (max 30 seconds)
            for (int i = 0; i < 30; i++)
            {
                await Task.Delay(1000, token);
                if (await IsRunningAsync(token))
                {
                    UpdateStatus(OllamaStatus.Running);
                    return true;
                }
            }

            UpdateStatus(OllamaStatus.Error);
            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to start Ollama: {ex.Message}");
            UpdateStatus(OllamaStatus.Error);
            return false;
        }
    }

    public async Task<List<OllamaModel>> GetModelsAsync(CancellationToken token = default)
    {
        try
        {
            var response = await _client.GetFromJsonAsync<OllamaTagsResponse>("/api/tags", token);
            var models = new List<OllamaModel>();

            if (response?.Models != null)
            {
                foreach (var m in response.Models)
                {
                    var sizeStr = FormatBytes(m.Size);
                    models.Add(new OllamaModel(m.Name, sizeStr, m.ModifiedAt));
                }
            }

            return models;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to get models: {ex.Message}");
            return new List<OllamaModel>();
        }
    }

    public async Task<bool> HasModelAsync(string modelName, CancellationToken token = default)
    {
        var models = await GetModelsAsync(token);
        return models.Exists(m => m.Name.StartsWith(modelName, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<bool> PullModelAsync(string modelName, IProgress<ModelPullProgress>? progress = null, CancellationToken token = default)
    {
        try
        {
            var request = new { name = modelName, stream = true };
            var response = await _client.PostAsJsonAsync("/api/pull", request, token);

            if (!response.IsSuccessStatusCode)
                return false;

            using var stream = await response.Content.ReadAsStreamAsync(token);
            using var reader = new StreamReader(stream);

            while (!reader.EndOfStream && !token.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(token);
                if (string.IsNullOrEmpty(line)) continue;

                try
                {
                    var pullResponse = JsonSerializer.Deserialize<OllamaPullResponse>(line);
                    if (pullResponse != null)
                    {
                        progress?.Report(new ModelPullProgress(
                            pullResponse.Status ?? "Downloading...",
                            pullResponse.Completed,
                            pullResponse.Total
                        ));

                        if (pullResponse.Status == "success")
                            return true;
                    }
                }
                catch { /* Ignore parse errors for individual lines */ }
            }

            return await HasModelAsync(modelName, token);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to pull model: {ex.Message}");
            return false;
        }
    }

    public async Task<string?> GetVersionAsync(CancellationToken token = default)
    {
        try
        {
            var ollamaPath = GetOllamaPath();
            if (string.IsNullOrEmpty(ollamaPath))
                return null;

            var startInfo = new ProcessStartInfo
            {
                FileName = ollamaPath,
                Arguments = "--version",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            };

            using var process = Process.Start(startInfo);
            if (process == null) return null;

            var output = await process.StandardOutput.ReadToEndAsync(token);
            await process.WaitForExitAsync(token);

            return process.ExitCode == 0 ? output.Trim() : null;
        }
        catch
        {
            return null;
        }
    }

    public void OpenDownloadPage()
    {
        var url = "https://ollama.ai/download";
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo("cmd", $"/c start {url}") { CreateNoWindow = true });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", url);
            }
            else
            {
                Process.Start("xdg-open", url);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to open download page: {ex.Message}");
        }
    }

    private string? GetOllamaPath()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Check common Windows paths including winget installation
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            var paths = new[]
            {
                // Winget installation paths
                Path.Combine(localAppData, "Microsoft", "WinGet", "Packages", "Ollama.Ollama_Microsoft.Winget.Source_8wekyb3d8bbwe", "ollama.exe"),
                Path.Combine(programFiles, "Ollama", "ollama.exe"),
                Path.Combine(localAppData, "Programs", "Ollama", "ollama.exe"),
                // Standard installation
                Path.Combine(userProfile, "AppData", "Local", "Programs", "Ollama", "ollama.exe"),
                // In PATH (most reliable for winget)
                "ollama.exe",
                "ollama"
            };

            // First try PATH-based execution (works best with winget)
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "ollama",
                    Arguments = "--version",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var p = Process.Start(psi);
                if (p != null)
                {
                    p.WaitForExit(5000);
                    if (p.ExitCode == 0) return "ollama";
                }
            }
            catch { }

            // Then check specific paths
            foreach (var path in paths)
            {
                if (File.Exists(path))
                {
                    try
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = path,
                            Arguments = "--version",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true
                        };
                        using var p = Process.Start(psi);
                        p?.WaitForExit(5000);
                        if (p?.ExitCode == 0) return path;
                    }
                    catch { }
                }
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var paths = new[] { "/usr/local/bin/ollama", "/opt/homebrew/bin/ollama", "ollama" };
            foreach (var path in paths)
            {
                if (File.Exists(path) || path == "ollama") return path;
            }
        }
        else
        {
            // Linux
            var paths = new[] { "/usr/local/bin/ollama", "/usr/bin/ollama", "ollama" };
            foreach (var path in paths)
            {
                if (File.Exists(path) || path == "ollama") return path;
            }
        }

        return null;
    }

    private void UpdateStatus(OllamaStatus status)
    {
        if (_currentStatus != status)
        {
            _currentStatus = status;
            OnStatusChanged?.Invoke(status);
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.#} {sizes[order]}";
    }

    // JSON response types
    private class OllamaTagsResponse
    {
        [JsonPropertyName("models")]
        public List<OllamaModelInfo>? Models { get; set; }
    }

    private class OllamaModelInfo
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("size")]
        public long Size { get; set; }

        [JsonPropertyName("modified_at")]
        public string ModifiedAt { get; set; } = "";
    }

    private class OllamaPullResponse
    {
        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("completed")]
        public long Completed { get; set; }

        [JsonPropertyName("total")]
        public long Total { get; set; }
    }
}
