using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace LocLM.Services;

public interface IPythonBackendService
{
    bool IsRunning { get; }
    event Action<string>? OnLog;
    event Action<string>? OnError;
    Task<bool> StartAsync();
    void Stop();
}

public class PythonBackendService : IPythonBackendService
{
    private readonly IPlatformService _platform;
    private Process? _process;
    private Process? _ollamaProcess;

    public bool IsRunning => _process != null && !_process.HasExited;
    public event Action<string>? OnLog;
    public event Action<string>? OnError;

    public PythonBackendService(IPlatformService platform)
    {
        _platform = platform;
    }

    public async Task<bool> StartAsync()
    {
        var backendPath = GetBackendPath();
        var mainScript = Path.Combine(backendPath, "main.py");

        if (!File.Exists(mainScript))
        {
            OnError?.Invoke($"Backend not found at {mainScript}");
            return false;
        }

        // If the app is configured for Ollama, ensure a fresh instance is running
        var provider = Environment.GetEnvironmentVariable("LLM_PROVIDER") ?? "groq";
        if (provider.Equals("ollama", StringComparison.OrdinalIgnoreCase))
        {
            var ok = await EnsureOllamaRunningAsync();
            if (!ok)
            {
                OnError?.Invoke("Failed to start Ollama. Check installation and try again.");
                return false;
            }
        }

        var psi = new ProcessStartInfo
        {
            FileName = _platform.GetPythonCommand(),
            Arguments = mainScript,
            WorkingDirectory = backendPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
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

        try
        {
            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            OnLog?.Invoke($"Python backend started (PID: {_process.Id})");

            // Give it time to start
            await Task.Delay(2000);
            return true;
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"Failed to start backend: {ex.Message}");
            return false;
        }
    }

    public void Stop()
    {
        if (_process != null && !_process.HasExited)
        {
            try
            {
                // Try graceful shutdown
                if (_process.StartInfo.RedirectStandardInput)
                {
                    try { _process.StandardInput.WriteLine("\u0003"); } catch { }
                }
                if (!_process.WaitForExit(3000))
                {
                    try { _process.CloseMainWindow(); } catch { }
                    if (!_process.WaitForExit(2000))
                    {
                        _process.Kill(true);
                    }
                }
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"Error stopping backend: {ex.Message}");
                try { _process.Kill(true); } catch { }
            }
            finally
            {
                _process.Dispose();
                _process = null;
                OnLog?.Invoke("Python backend stopped");
            }
        }

        if (_ollamaProcess != null && !_ollamaProcess.HasExited)
        {
            try { _ollamaProcess.Kill(true); } catch { }
            try { _ollamaProcess.Dispose(); } catch { }
            _ollamaProcess = null;
            OnLog?.Invoke("Ollama process stopped");
        }
    }

    private string GetBackendPath()
    {
        // Prefer the assembly directory (works in published builds)
        var assemblyDir = Path.GetDirectoryName(typeof(PythonBackendService).Assembly.Location)
            ?? AppContext.BaseDirectory;
        var backendPath = Path.Combine(assemblyDir, "backend");

        if (!Directory.Exists(backendPath))
        {
            // Try AppContext base
            backendPath = Path.Combine(AppContext.BaseDirectory, "backend");
        }
        if (!Directory.Exists(backendPath))
        {
            // Fallback to current directory
            backendPath = Path.Combine(Directory.GetCurrentDirectory(), "backend");
        }

        return backendPath;
    }

    private async Task<bool> EnsureOllamaRunningAsync()
    {
        try
        {
            // Kill any stale Ollama instances to avoid port conflicts
            foreach (var p in Process.GetProcesses().Where(p =>
                         p.ProcessName.Equals("ollama", StringComparison.OrdinalIgnoreCase)))
            {
                try { p.Kill(true); p.WaitForExit(1000); } catch { }
            }
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"Failed to clean up existing Ollama processes: {ex.Message}");
        }

        var ollamaExe = "ollama";
        var ollamaUrl = Environment.GetEnvironmentVariable("OLLAMA_URL") ?? "http://localhost:11434";

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ollamaExe,
                Arguments = "serve",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            _ollamaProcess = Process.Start(psi);
            if (_ollamaProcess == null || _ollamaProcess.HasExited)
            {
                OnError?.Invoke("Could not start Ollama (process exited immediately).");
                return false;
            }

            OnLog?.Invoke($"Ollama started (PID: {_ollamaProcess.Id})");

            // Poll the Ollama API for readiness
            using var client = new HttpClient();
            var attempts = 0;
            while (attempts < 12)
            {
                attempts++;
                try
                {
                    var resp = await client.GetAsync($"{ollamaUrl}/api/tags");
                    if (resp.IsSuccessStatusCode)
                    {
                        OnLog?.Invoke("Ollama is healthy and accepting requests.");
                        return true;
                    }
                }
                catch { /* keep polling */ }

                await Task.Delay(1000);
            }

            OnError?.Invoke($"Ollama did not become ready at {ollamaUrl}.");
            return false;
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"Failed to start Ollama: {ex.Message}");
            return false;
        }
    }
}
