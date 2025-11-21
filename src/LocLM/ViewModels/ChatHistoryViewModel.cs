using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocLM.Models;
using LocLM.Services;

namespace LocLM.ViewModels;

public partial class ChatHistoryViewModel : ObservableObject
{
    private readonly IChatHistoryService _chatHistory;
    private readonly MainWindowViewModel _mainWindow;

    public ObservableCollection<ChatSessionViewModel> Sessions { get; } = new();

    [ObservableProperty]
    private ChatSessionViewModel? _selectedSession;

    public ChatHistoryViewModel(IChatHistoryService chatHistory, MainWindowViewModel mainWindow)
    {
        _chatHistory = chatHistory;
        _mainWindow = mainWindow;

        _ = LoadSessionsAsync();
    }

    public async Task LoadSessionsAsync()
    {
        var sessions = await _chatHistory.GetAllSessionsAsync();
        Sessions.Clear();
        foreach (var session in sessions)
        {
            Sessions.Add(new ChatSessionViewModel(session, this));
        }
    }

    public async Task SelectSessionAsync(ChatSessionViewModel session)
    {
        SelectedSession = session;
        await _mainWindow.LoadSessionAsync(session.Id);
    }

    public async Task DeleteSessionAsync(ChatSessionViewModel session)
    {
        await _chatHistory.DeleteSessionAsync(session.Id);
        Sessions.Remove(session);
    }

    [RelayCommand]
    private async Task NewSessionAsync()
    {
        await _mainWindow.NewSessionAsync();
        SelectedSession = null;
    }
}

public partial class ChatSessionViewModel : ObservableObject
{
    private readonly ChatSession _session;
    private readonly ChatHistoryViewModel _parent;

    public int Id => _session.Id;
    public string Title => _session.Title;
    public string ModelName => _session.ModelName;
    public string Mode => _session.Mode;
    public DateTime CreatedAt => _session.CreatedAt;
    public DateTime UpdatedAt => _session.UpdatedAt;

    public string TimeAgo
    {
        get
        {
            var diff = DateTime.UtcNow - UpdatedAt;
            if (diff.TotalMinutes < 1) return "Just now";
            if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m ago";
            if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h ago";
            if (diff.TotalDays < 7) return $"{(int)diff.TotalDays}d ago";
            return UpdatedAt.ToLocalTime().ToString("MMM d");
        }
    }

    public string ModeIcon => Mode == "agent" ? "🤖" : "💬";

    public ChatSessionViewModel(ChatSession session, ChatHistoryViewModel parent)
    {
        _session = session;
        _parent = parent;
    }

    [RelayCommand]
    private async Task SelectAsync()
    {
        await _parent.SelectSessionAsync(this);
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        await _parent.DeleteSessionAsync(this);
    }
}
