using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace LocLM.Services;

public interface IPythonBackendService
{
    bool IsRunning { get; }
    event Action<string>? OnLog;
    event Action<string>? OnError;
    Task StartAsync();
    void Stop();
}

public class PythonBackendService : IPythonBackendService
{
    private readonly IPlatformService _platform;
    private Process? _process;

    public bool IsRunning => _process != null && !_process.HasExited;
    public event Action<string>? OnLog;
    public event Action<string>? OnError;

    public PythonBackendService(IPlatformService platform)
    {
        _platform = platform;
    }

    public async Task StartAsync()
    {
        var backendPath = GetBackendPath();
        var mainScript = Path.Combine(backendPath, "main.py");

        if (!File.Exists(mainScript))
        {
            OnError?.Invoke($"Backend not found at {mainScript}");
            return;
        }

        var psi = new ProcessStartInfo
        {
            FileName = _platform.GetPythonCommand(),
            Arguments = mainScript,
            WorkingDirectory = backendPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        psi.Environment["PYTHONUNBUFFERED"] = "1";

        // Set LLM provider (use environment variable or default to Groq)
        psi.Environment["LLM_PROVIDER"] = Environment.GetEnvironmentVariable("LLM_PROVIDER") ?? "groq";

        // Pass through Groq API key and model if configured in the environment
        var groqApiKey = Environment.GetEnvironmentVariable("GROQ_API_KEY");
        if (!string.IsNullOrWhiteSpace(groqApiKey))
        {
            psi.Environment["GROQ_API_KEY"] = groqApiKey;
        }
        var groqModel = Environment.GetEnvironmentVariable("GROQ_MODEL");
        if (!string.IsNullOrWhiteSpace(groqModel))
        {
            psi.Environment["GROQ_MODEL"] = groqModel;
        }

        // Pass Ollama preferences if present
        var ollamaModel = Environment.GetEnvironmentVariable("OLLAMA_MODEL");
        if (!string.IsNullOrWhiteSpace(ollamaModel))
        {
            psi.Environment["OLLAMA_MODEL"] = ollamaModel;
        }
        var ollamaUrl = Environment.GetEnvironmentVariable("OLLAMA_URL");
        if (!string.IsNullOrWhiteSpace(ollamaUrl))
        {
            psi.Environment["OLLAMA_URL"] = ollamaUrl;
        }

        _process = new Process { StartInfo = psi };
        _process.OutputDataReceived += (s, e) =>
        {
            if (e.Data != null) OnLog?.Invoke(e.Data);
        };
        _process.ErrorDataReceived += (s, e) =>
        {
            if (e.Data != null) OnError?.Invoke(e.Data);
        };

        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        OnLog?.Invoke($"Python backend started (PID: {_process.Id})");

        // Give it time to start
        await Task.Delay(2000);
    }

    public void Stop()
    {
        if (_process != null && !_process.HasExited)
        {
            _process.Kill();
            _process.Dispose();
            _process = null;
            OnLog?.Invoke("Python backend stopped");
        }
    }

    private string GetBackendPath()
    {
        // In development, backend is next to the executable
        var basePath = AppContext.BaseDirectory;
        var backendPath = Path.Combine(basePath, "backend");

        if (!Directory.Exists(backendPath))
        {
            // Try relative to project
            backendPath = Path.Combine(Directory.GetCurrentDirectory(), "backend");
        }

        return backendPath;
    }
}
