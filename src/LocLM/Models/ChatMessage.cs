using System;

namespace LocLM.Models;

public class ChatMessage
{
    public int Id { get; set; }
    public int SessionId { get; set; }
    public string Role { get; set; } = string.Empty; // "user", "assistant", "system", "error"
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
