using System.Collections.Generic;
using System.Threading.Tasks;
using LocLM.Models;

namespace LocLM.Services;

public interface IChatHistoryService
{
    Task InitializeAsync();
    Task<int> CreateSessionAsync(string title, string modelName, string mode);
    Task<List<ChatSession>> GetAllSessionsAsync();
    Task<ChatSession?> GetSessionAsync(int sessionId);
    Task UpdateSessionAsync(int sessionId, string title);
    Task DeleteSessionAsync(int sessionId);
    Task<int> AddMessageAsync(int sessionId, string role, string content);
    Task<List<ChatMessage>> GetSessionMessagesAsync(int sessionId);
    Task UpdateSessionTimestampAsync(int sessionId);
}
