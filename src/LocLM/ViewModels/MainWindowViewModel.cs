using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocLM.Services;

namespace LocLM.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IAgentService _agentService;
    private readonly IPythonBackendService _pythonBackend;
    private readonly IOllamaService _ollamaService;
    private readonly IFileSystemService _fileSystem;
    private readonly IChatHistoryService _chatHistory;

    [ObservableProperty]
    private string _inputText = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _connectionStatus = "Connecting...";

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private string _modelName = "Connecting...";

    [ObservableProperty]
    private string _providerName = "";

    [ObservableProperty]
    private bool _modelAvailable;

    [ObservableProperty]
    private string _workingDirectory = "./";

    [ObservableProperty]
    private string _platform = "Windows";

    [ObservableProperty]
    private string _currentMode = "chat";

    [ObservableProperty]
    private bool _isChatMode = true;

    [ObservableProperty]
    private bool _isSettingsOpen;

    [ObservableProperty]
    private bool _isModelSelectorOpen;

    [ObservableProperty]
    private bool _isOllamaInstalled;

    [ObservableProperty]
    private bool _isOllamaRunning;

    [ObservableProperty]
    private string _ollamaStatus = "Checking...";

    [ObservableProperty]
    private bool _isSetupOpen;

    [ObservableProperty]
    private bool _isDownloadingModel;

    [ObservableProperty]
    private double _downloadProgress;

    [ObservableProperty]
    private string _downloadStatus = "";

    public ObservableCollection<ChatMessage> Messages { get; } = new();
    public ObservableCollection<string> OllamaModels { get; } = new();

    // File Explorer
    public FileExplorerViewModel FileExplorer { get; }

    // Editor with tabs
    public EditorViewModel Editor { get; }

    // View mode: "chat" or "editor"
    [ObservableProperty]
    private string _viewMode = "chat";

    [ObservableProperty]
    private bool _isEditorView;

    // Keyboard shortcuts
    public KeyboardShortcutsViewModel KeyboardShortcuts { get; }

    // Chat history
    public ChatHistoryViewModel ChatHistory { get; }

    [ObservableProperty]
    private bool _isKeyboardShortcutsOpen;

    [ObservableProperty]
    private string _vimMode = "NORMAL";

    [ObservableProperty]
    private bool _showSidebar = true;

    [ObservableProperty]
    private int? _currentSessionId;

    public MainWindowViewModel(IAgentService agentService, IPythonBackendService pythonBackend, IOllamaService ollamaService, IFileSystemService fileSystem, IKeyboardService keyboardService, IChatHistoryService chatHistory)
    {
        _agentService = agentService;
        _pythonBackend = pythonBackend;
        _ollamaService = ollamaService;
        _fileSystem = fileSystem;
        _chatHistory = chatHistory;

        // Initialize keyboard shortcuts
        KeyboardShortcuts = new KeyboardShortcutsViewModel(keyboardService);
        keyboardService.OnVimModeChanged += mode => VimMode = mode;

        // Initialize file explorer
        FileExplorer = new FileExplorerViewModel(fileSystem);

        // Initialize editor
        Editor = new EditorViewModel(fileSystem);

        // Initialize chat history
        ChatHistory = new ChatHistoryViewModel(chatHistory, this);

        // Wire up file opening from explorer to editor
        FileExplorer.OnFileOpened += async (path, content) =>
        {
            await Editor.OpenFileAsync(path);
            ViewMode = "editor";
            IsEditorView = true;
        };

        // Set platform info
        Platform = OperatingSystem.IsWindows() ? "Windows" :
                   OperatingSystem.IsLinux() ? "Linux" : "macOS";
        WorkingDirectory = Environment.CurrentDirectory;

        _pythonBackend.OnLog += log => System.Diagnostics.Debug.WriteLine($"[Python] {log}");
        _pythonBackend.OnError += err => System.Diagnostics.Debug.WriteLine($"[Python Error] {err}");

        _ollamaService.OnStatusChanged += status =>
        {
            OllamaStatus = status.ToString();
            IsOllamaRunning = status == Services.OllamaStatus.Running;
        };

        // Check connection periodically
        _ = CheckConnectionLoop();
        _ = CheckOllamaStatus();

        // Load current directory in file explorer
        _ = FileExplorer.LoadDirectoryAsync(WorkingDirectory);
    }

    private async Task CheckConnectionLoop()
    {
        while (true)
        {
            IsConnected = await _agentService.CheckHealthAsync();
            ConnectionStatus = IsConnected ? "Connected" : "Connecting...";

            if (IsConnected)
            {
                var modelInfo = await _agentService.GetModelInfoAsync();
                ModelName = modelInfo.Model;
                ProviderName = modelInfo.Provider;
                ModelAvailable = modelInfo.Available;

                CurrentMode = await _agentService.GetModeAsync();
                IsChatMode = CurrentMode == "chat";
            }
            else
            {
                ModelName = "Offline";
                ProviderName = "";
                ModelAvailable = false;
            }

            await Task.Delay(3000);
        }
    }

    [RelayCommand]
    private async Task ToggleModeAsync()
    {
        var newMode = IsChatMode ? "agent" : "chat";
        CurrentMode = await _agentService.SetModeAsync(newMode);
        IsChatMode = CurrentMode == "chat";
    }

    [RelayCommand]
    private async Task RefreshConnectionAsync()
    {
        IsConnected = await _agentService.CheckHealthAsync();
        ConnectionStatus = IsConnected ? "Connected" : "Connecting...";

        if (IsConnected)
        {
            var modelInfo = await _agentService.GetModelInfoAsync();
            ModelName = modelInfo.Model;
            ProviderName = modelInfo.Provider;
            ModelAvailable = modelInfo.Available;
        }
    }

    [RelayCommand]
    private void NewSession()
    {
        Messages.Clear();
    }

    [RelayCommand]
    private void ToggleSettings()
    {
        IsSettingsOpen = !IsSettingsOpen;
    }

    [RelayCommand]
    private void ToggleModelSelector()
    {
        IsModelSelectorOpen = !IsModelSelectorOpen;
    }

    [RelayCommand]
    private void SwitchToChat()
    {
        ViewMode = "chat";
        IsEditorView = false;
    }

    [RelayCommand]
    private void SwitchToEditor()
    {
        ViewMode = "editor";
        IsEditorView = true;
    }

    [RelayCommand]
    private void ToggleKeyboardShortcuts()
    {
        IsKeyboardShortcutsOpen = !IsKeyboardShortcutsOpen;
    }

    [RelayCommand]
    private void ToggleSidebar()
    {
        ShowSidebar = !ShowSidebar;
    }

    [RelayCommand]
    private void SetVimMode(string mode)
    {
        KeyboardShortcuts.SetVimMode(mode);
    }

    private async Task CheckOllamaStatus()
    {
        // First check if server is running (most reliable indicator)
        IsOllamaRunning = await _ollamaService.IsRunningAsync();

        if (IsOllamaRunning)
        {
            IsOllamaInstalled = true;
            OllamaStatus = "Running";

            // Fetch installed models
            var models = await _ollamaService.GetModelsAsync();
            OllamaModels.Clear();
            foreach (var model in models)
                OllamaModels.Add(model.Name);

            System.Diagnostics.Debug.WriteLine($"[Ollama] Found {models.Count} models");
        }
        else
        {
            // Check if binary is installed
            IsOllamaInstalled = await _ollamaService.IsInstalledAsync();

            if (IsOllamaInstalled)
            {
                OllamaStatus = "Starting...";
                // Auto-start Ollama if installed but not running
                var started = await _ollamaService.StartServerAsync();
                if (started)
                {
                    IsOllamaRunning = true;
                    OllamaStatus = "Running";
                    // Fetch models after starting
                    var models = await _ollamaService.GetModelsAsync();
                    OllamaModels.Clear();
                    foreach (var model in models)
                        OllamaModels.Add(model.Name);
                }
                else
                {
                    OllamaStatus = "Installed (click to start)";
                }
            }
            else
            {
                OllamaStatus = "Not Installed";
            }
        }
    }

    [RelayCommand]
    private void OpenOllamaDownload()
    {
        _ollamaService.OpenDownloadPage();
    }

    [RelayCommand]
    private async Task StartOllamaAsync()
    {
        OllamaStatus = "Starting...";
        var started = await _ollamaService.StartServerAsync();
        if (started)
        {
            OllamaStatus = "Running";
            IsOllamaRunning = true;
            await CheckOllamaStatus();
        }
        else
        {
            OllamaStatus = "Failed to start";
        }
    }

    [RelayCommand]
    private async Task PullModelAsync(string modelName)
    {
        if (IsDownloadingModel) return;

        IsDownloadingModel = true;
        DownloadProgress = 0;
        DownloadStatus = $"Downloading {modelName}...";

        var progress = new Progress<ModelPullProgress>(p =>
        {
            DownloadProgress = p.Percentage;
            DownloadStatus = $"{p.Status} ({p.Percentage:F1}%)";
        });

        var success = await _ollamaService.PullModelAsync(modelName, progress);

        IsDownloadingModel = false;
        DownloadStatus = success ? "Download complete!" : "Download failed";

        if (success)
        {
            await CheckOllamaStatus();
        }
    }

    [RelayCommand]
    private async Task SwitchToOllamaAsync()
    {
        // Use first available model if we have any
        var model = OllamaModels.Count > 0 ? OllamaModels[0] : "llama3.2";
        await _agentService.SetProviderAsync("ollama", model);
        await RefreshConnectionAsync();
        IsModelSelectorOpen = false;
    }

    [RelayCommand]
    private async Task SelectOllamaModelAsync(string modelName)
    {
        await _agentService.SetProviderAsync("ollama", modelName);
        await RefreshConnectionAsync();
        IsModelSelectorOpen = false;
    }

    [RelayCommand]
    private async Task SwitchToGroqAsync()
    {
        await _agentService.SetProviderAsync("groq", "llama-3.3-70b-versatile");
        await RefreshConnectionAsync();
        IsModelSelectorOpen = false;
    }

    [RelayCommand]
    private void ToggleSetup()
    {
        IsSetupOpen = !IsSetupOpen;
    }

    [RelayCommand]
    private async Task SendMessageAsync()
    {
        if (string.IsNullOrWhiteSpace(InputText) || IsLoading)
            return;

        var userMessage = InputText;
        InputText = string.Empty;

        // Create new session if none exists
        if (CurrentSessionId == null)
        {
            var title = userMessage.Length > 50 ? userMessage.Substring(0, 50) + "..." : userMessage;
            CurrentSessionId = await _chatHistory.CreateSessionAsync(title, ModelName, CurrentMode);

            // Refresh chat history list
            await ChatHistory.LoadSessionsAsync();
        }

        Messages.Add(new ChatMessage("user", userMessage));

        // Save user message to database
        if (CurrentSessionId.HasValue)
        {
            await _chatHistory.AddMessageAsync(CurrentSessionId.Value, "user", userMessage);
        }

        IsLoading = true;

        try
        {
            var response = await _agentService.SendPromptAsync(userMessage);
            Messages.Add(new ChatMessage("assistant", response.Response));

            // Save assistant message to database
            if (CurrentSessionId.HasValue)
            {
                await _chatHistory.AddMessageAsync(CurrentSessionId.Value, "assistant", response.Response);
                await _chatHistory.UpdateSessionTimestampAsync(CurrentSessionId.Value);

                // Refresh to update timestamp display
                await ChatHistory.LoadSessionsAsync();
            }

            // Show tool calls if any
            if (response.ToolCalls != null)
            {
                foreach (var tool in response.ToolCalls)
                {
                    var toolMessage = $"[Tool: {tool.Tool}] {tool.Result}";
                    Messages.Add(new ChatMessage("system", toolMessage));

                    // Save tool output to database
                    if (CurrentSessionId.HasValue)
                    {
                        await _chatHistory.AddMessageAsync(CurrentSessionId.Value, "system", toolMessage);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            var errorMessage = $"Error: {ex.Message}";
            Messages.Add(new ChatMessage("error", errorMessage));

            // Save error to database
            if (CurrentSessionId.HasValue)
            {
                await _chatHistory.AddMessageAsync(CurrentSessionId.Value, "error", errorMessage);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task NewSessionAsync()
    {
        Messages.Clear();
        CurrentSessionId = null;
    }

    public async Task LoadSessionAsync(int sessionId)
    {
        Messages.Clear();
        CurrentSessionId = sessionId;

        var messages = await _chatHistory.GetSessionMessagesAsync(sessionId);
        foreach (var msg in messages)
        {
            Messages.Add(new ChatMessage(msg.Role, msg.Content));
        }

        // Switch to chat view
        ViewMode = "chat";
        IsEditorView = false;
    }
}

public record ChatMessage(string Role, string Content)
{
    public bool IsUser => Role == "user";
    public bool IsAssistant => Role == "assistant";
    public bool IsSystem => Role == "system";
    public bool IsError => Role == "error";
}
