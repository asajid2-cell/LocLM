using System;

namespace LocLM.Models;

public class ChatSession
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string ModelName { get; set; } = string.Empty;
    public string Mode { get; set; } = "chat"; // "chat" or "agent"
}
