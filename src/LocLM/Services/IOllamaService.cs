using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LocLM.Services;

public interface IOllamaService
{
    /// <summary>
    /// Check if Ollama is installed on the system
    /// </summary>
    Task<bool> IsInstalledAsync(CancellationToken token = default);

    /// <summary>
    /// Check if Ollama server is running
    /// </summary>
    Task<bool> IsRunningAsync(CancellationToken token = default);

    /// <summary>
    /// Start the Ollama server
    /// </summary>
    Task<bool> StartServerAsync(CancellationToken token = default);

    /// <summary>
    /// Get list of installed models
    /// </summary>
    Task<List<OllamaModel>> GetModelsAsync(CancellationToken token = default);

    /// <summary>
    /// Check if a specific model is installed
    /// </summary>
    Task<bool> HasModelAsync(string modelName, CancellationToken token = default);

    /// <summary>
    /// Pull/download a model with progress reporting
    /// </summary>
    Task<bool> PullModelAsync(string modelName, IProgress<ModelPullProgress>? progress = null, CancellationToken token = default);

    /// <summary>
    /// Get the Ollama version
    /// </summary>
    Task<string?> GetVersionAsync(CancellationToken token = default);

    /// <summary>
    /// Open the Ollama download page
    /// </summary>
    void OpenDownloadPage();

    /// <summary>
    /// Event fired when Ollama status changes
    /// </summary>
    event Action<OllamaStatus>? OnStatusChanged;
}

public record OllamaModel(string Name, string Size, string ModifiedAt);

public record ModelPullProgress(string Status, long Completed, long Total)
{
    public double Percentage => Total > 0 ? (double)Completed / Total * 100 : 0;
}

public enum OllamaStatus
{
    NotInstalled,
    Installed,
    Starting,
    Running,
    Error
}
