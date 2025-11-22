using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
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
    private readonly System.Threading.CancellationTokenSource _cancellationTokenSource = new();
    private bool _workspaceInitialized = false;

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
    private string _currentMode = "agent";  // Default to agent mode

    [ObservableProperty]
    private bool _isChatMode = false;  // Default to agent mode (false = agent, true = chat)

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

    // Terminal Manager
    public TerminalManagerViewModel TerminalManager { get; }

    // View mode: "chat" or "editor"
    [ObservableProperty]
    private string _viewMode = "chat";

    [ObservableProperty]
    private bool _isEditorView;

    [ObservableProperty]
    private bool _isTerminalVisible;

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

    [ObservableProperty]
    private bool _isFileMenuOpen;

    [ObservableProperty]
    private bool _isEditMenuOpen;

    [ObservableProperty]
    private bool _isViewMenuOpen;

    [ObservableProperty]
    private bool _isRunMenuOpen;

    [ObservableProperty]
    private bool _isTerminalMenuOpen;

    public MainWindowViewModel(IAgentService agentService, IPythonBackendService pythonBackend, IOllamaService ollamaService, IFileSystemService fileSystem, IKeyboardService keyboardService, IChatHistoryService chatHistory, ITerminalService terminal)
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

        // Initialize terminal manager
        TerminalManager = new TerminalManagerViewModel(terminal);

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

        // Don't load any folder by default - user will open one
        WorkingDirectory = "No folder opened";

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
    }

    private static string? FindWorkspaceRoot()
    {
        // Try to anchor the explorer to the solution root (LocLM.sln) if we're running from bin/Debug
        var dir = new DirectoryInfo(Environment.CurrentDirectory);
        while (dir != null)
        {
            var sln = Path.Combine(dir.FullName, "LocLM.sln");
            if (File.Exists(sln))
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    private async Task CheckConnectionLoop()
    {
        try
        {
            while (!_cancellationTokenSource.Token.IsCancellationRequested)
            {
                try
                {
                    IsConnected = await _agentService.CheckHealthAsync();
                    ConnectionStatus = IsConnected ? "Connected" : "Connecting...";

                    if (IsConnected)
                    {
                        var modelInfo = await _agentService.GetModelInfoAsync();
                        ModelName = modelInfo.Model;
                        ProviderName = modelInfo.Provider;
                        ModelAvailable = modelInfo.Available;

                        // Update connection status based on model availability
                        if (!ModelAvailable)
                        {
                            ConnectionStatus = $"Model not available";
                        }
                        else
                        {
                            ConnectionStatus = "Ready";
                        }

                        CurrentMode = await _agentService.GetModeAsync();
                        IsChatMode = CurrentMode == "chat";

                        // Initialize workspace on first successful connection
                        if (!_workspaceInitialized)
                        {
                            var workspaceRoot = FindWorkspaceRoot();
                            if (workspaceRoot != null)
                            {
                                try
                                {
                                    await _agentService.SetWorkspaceAsync(workspaceRoot);
                                    WorkingDirectory = workspaceRoot;
                                    await FileExplorer.LoadDirectoryAsync(workspaceRoot);
                                    System.Diagnostics.Debug.WriteLine($"[Workspace] Auto-initialized workspace to: {workspaceRoot}");
                                    _workspaceInitialized = true;
                                }
                                catch (Exception ex)
                                {
                                    System.Diagnostics.Debug.WriteLine($"[Workspace] Failed to auto-initialize workspace: {ex.Message}");
                                }
                            }
                        }
                    }
                    else
                    {
                        ModelName = "Offline";
                        ProviderName = "";
                        ModelAvailable = false;
                    }
                }
                catch (Exception ex)
                {
                    // Don't crash on connection errors
                    System.Diagnostics.Debug.WriteLine($"[Connection Error] {ex.Message}");
                    IsConnected = false;
                }

                // Use cancellable delay
                await Task.Delay(5000, _cancellationTokenSource.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation, exit gracefully
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
    public async Task NewSessionAsync()
    {
        Messages.Clear();
        CurrentSessionId = null;

        // Switch to chat view
        ViewMode = "chat";
        IsEditorView = false;

        // Clear backend conversation history
        try
        {
            await _agentService.ClearHistoryAsync();
        }
        catch { }
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

    [RelayCommand]
    private void ToggleTerminal()
    {
        IsTerminalVisible = !IsTerminalVisible;
    }

    [RelayCommand]
    private async Task RunCurrentFileAsync()
    {
        if (Editor.ActiveTab == null)
        {
            Messages.Add(new ChatMessage("system", "No file is currently open"));
            return;
        }

        // Auto-save if dirty
        if (Editor.ActiveTab.IsDirty)
        {
            await Editor.SaveActiveTabCommand.ExecuteAsync(null);
        }

        var filePath = Editor.ActiveTab.FilePath;
        var extension = Path.GetExtension(filePath).ToLowerInvariant();

        // Show and focus terminal
        IsTerminalVisible = true;

        // Determine run command based on file extension
        string? command = extension switch
        {
            ".py" => $"python \"{filePath}\"",
            ".cs" => $"dotnet run \"{filePath}\"",
            ".js" => $"node \"{filePath}\"",
            ".ts" => $"ts-node \"{filePath}\"",
            ".sh" => $"bash \"{filePath}\"",
            ".ps1" => $"powershell -File \"{filePath}\"",
            ".java" => $"java \"{filePath}\"",
            ".rb" => $"ruby \"{filePath}\"",
            ".go" => $"go run \"{filePath}\"",
            _ => null
        };

        if (command != null && TerminalManager.ActiveTerminal != null)
        {
            TerminalManager.ActiveTerminal.CurrentCommand = command;
            await TerminalManager.ActiveTerminal.ExecuteCommandCommand.ExecuteAsync(null);
        }
        else
        {
            Messages.Add(new ChatMessage("system", $"Unable to run {extension} files automatically"));
        }
    }

    [RelayCommand]
    private void ToggleFileMenu()
    {
        IsFileMenuOpen = !IsFileMenuOpen;
        IsEditMenuOpen = false;
        IsViewMenuOpen = false;
        IsRunMenuOpen = false;
    }

    [RelayCommand]
    private async Task OpenFolderAsync()
    {
        // Close the file menu
        IsFileMenuOpen = false;

        // Use Avalonia's folder picker
        var window = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;

        if (window != null)
        {
            var folder = await window.StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
            {
                Title = "Open Folder",
                AllowMultiple = false
            });

            if (folder.Count > 0)
            {
                var selectedPath = folder[0].Path.LocalPath;
                WorkingDirectory = selectedPath;
                await FileExplorer.LoadDirectoryAsync(selectedPath);

                // Set terminal working directory
                TerminalManager.SetWorkingDirectory(selectedPath);

                // Sync workspace with backend for agent tools
                try
                {
                    await _agentService.SetWorkspaceAsync(selectedPath);
                    System.Diagnostics.Debug.WriteLine($"[Workspace] Set backend workspace to: {selectedPath}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Workspace] Failed to set backend workspace: {ex.Message}");
                }
            }
        }
    }

    [RelayCommand]
    private void ToggleEditMenu()
    {
        IsEditMenuOpen = !IsEditMenuOpen;
        IsFileMenuOpen = false;
        IsViewMenuOpen = false;
        IsRunMenuOpen = false;
    }

    [RelayCommand]
    private void ToggleViewMenu()
    {
        IsViewMenuOpen = !IsViewMenuOpen;
        IsFileMenuOpen = false;
        IsEditMenuOpen = false;
        IsRunMenuOpen = false;
    }

    [RelayCommand]
    private void ToggleRunMenu()
    {
        IsRunMenuOpen = !IsRunMenuOpen;
        IsFileMenuOpen = false;
        IsEditMenuOpen = false;
        IsViewMenuOpen = false;
        IsTerminalMenuOpen = false;
    }

    [RelayCommand]
    private void ToggleTerminalMenu()
    {
        IsTerminalMenuOpen = !IsTerminalMenuOpen;
        IsFileMenuOpen = false;
        IsEditMenuOpen = false;
        IsViewMenuOpen = false;
        IsRunMenuOpen = false;
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

            // Only refresh once when creating new session
            _ = ChatHistory.LoadSessionsAsync();
        }

        Messages.Add(new ChatMessage("user", userMessage));

        // Save user message to database (fire and forget - don't block UI)
        if (CurrentSessionId.HasValue)
        {
            _ = _chatHistory.AddMessageAsync(CurrentSessionId.Value, "user", userMessage);
        }

        IsLoading = true;

        try
        {
            var response = await _agentService.SendPromptAsync(userMessage);
            Messages.Add(new ChatMessage("assistant", response.Response));

            // Save assistant message to database (batch the updates)
            if (CurrentSessionId.HasValue)
            {
                // Do all database operations in one go
                await Task.WhenAll(
                    _chatHistory.AddMessageAsync(CurrentSessionId.Value, "assistant", response.Response),
                    _chatHistory.UpdateSessionTimestampAsync(CurrentSessionId.Value)
                );

                // Don't refresh history list on every message - only on new session
                // This prevents the expensive LoadSessionsAsync on every message
            }

            // Show tool calls if any
            if (response.ToolCalls != null && response.ToolCalls.Count > 0)
            {
                // Batch tool call messages
                var toolTasks = new List<Task>();
                foreach (var tool in response.ToolCalls)
                {
                    var toolMessage = $"[Tool: {tool.Tool}] {tool.Result}";
                    Messages.Add(new ChatMessage("system", toolMessage));

                    // Save tool output to database (don't await - batch it)
                    if (CurrentSessionId.HasValue)
                    {
                        toolTasks.Add(_chatHistory.AddMessageAsync(CurrentSessionId.Value, "system", toolMessage));
                    }
                }

                // Await all tool saves at once
                if (toolTasks.Count > 0)
                {
                    await Task.WhenAll(toolTasks);
                }

                // Check for file changes and display them
                try
                {
                    var fileChanges = await _agentService.GetFileChangesAsync();
                    if (fileChanges != null && fileChanges.TotalFiles > 0)
                    {
                        var changesSummary = FormatFileChangesSummary(fileChanges);
                        Messages.Add(new ChatMessage("system", changesSummary));

                        // Also refresh the file explorer to show new/modified files
                        if (!string.IsNullOrEmpty(WorkingDirectory) && WorkingDirectory != "No folder opened")
                        {
                            _ = FileExplorer.RefreshCurrentDirectoryAsync();
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[FileChanges] Failed to fetch changes: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            var errorMessage = $"Error: {ex.Message}";
            Messages.Add(new ChatMessage("error", errorMessage));

            // Save error to database (fire and forget)
            if (CurrentSessionId.HasValue)
            {
                _ = _chatHistory.AddMessageAsync(CurrentSessionId.Value, "error", errorMessage);
            }
        }
        finally
        {
            IsLoading = false;
        }
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

    private string FormatFileChangesSummary(FileChangeSummary changes)
    {
        var lines = new List<string>
        {
            "File Changes:",
            $"   Created: {changes.Created}",
            $"   Modified: {changes.Modified}",
            $"   Deleted: {changes.Deleted}",
            ""
        };

        foreach (var file in changes.Files)
        {
            var prefix = file.Operation.ToLower() switch
            {
                "create" => "[+]",
                "modify" => "[*]",
                "delete" => "[-]",
                _ => "   "
            };
            lines.Add($"   {prefix} {file.Path}");
        }

        return string.Join("\n", lines);
    }
}

public record ChatMessage(string Role, string Content)
{
    public bool IsUser => Role == "user";
    public bool IsAssistant => Role == "assistant";
    public bool IsSystem => Role == "system";
    public bool IsError => Role == "error";
}
