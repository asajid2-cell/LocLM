using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LocLM.Services;

public record ChatResponse(string Response, List<ToolCall>? ToolCalls);
public record ToolCall(string Tool, Dictionary<string, object>? Args, string? Result);
public record ModelInfo(string Provider, string Model, bool Available);

public record ProviderInfo(string Provider, string Model, List<string> AvailableProviders);

public interface IAgentService
{
    Task<ChatResponse> SendPromptAsync(string prompt, CancellationToken token = default);
    Task<List<string>> GetToolsAsync(CancellationToken token = default);
    Task<bool> CheckHealthAsync(CancellationToken token = default);
    Task<ModelInfo> GetModelInfoAsync(CancellationToken token = default);
    Task<string> GetModeAsync(CancellationToken token = default);
    Task<string> SetModeAsync(string mode, CancellationToken token = default);
    Task<ProviderInfo> GetProviderAsync(CancellationToken token = default);
    Task<ProviderInfo> SetProviderAsync(string provider, string? model = null, CancellationToken token = default);
}

public class AgentService : IAgentService
{
    private readonly HttpClient _client;
    private const string BaseUrl = "http://localhost:8000";

    public AgentService(HttpClient client)
    {
        _client = client;
        _client.BaseAddress = new Uri(BaseUrl);
        _client.Timeout = TimeSpan.FromMinutes(2);
    }

    public async Task<ChatResponse> SendPromptAsync(string prompt, CancellationToken token = default)
    {
        var request = new { prompt };
        var response = await _client.PostAsJsonAsync("/chat", request, token);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ChatResponse>(token);
        return result ?? new ChatResponse("No response", null);
    }

    public async Task<List<string>> GetToolsAsync(CancellationToken token = default)
    {
        var response = await _client.GetFromJsonAsync<JsonElement>("/tools", token);

        var tools = new List<string>();
        if (response.TryGetProperty("tools", out var toolsArray))
        {
            foreach (var tool in toolsArray.EnumerateArray())
            {
                if (tool.TryGetProperty("name", out var name))
                {
                    tools.Add(name.GetString() ?? "");
                }
            }
        }

        return tools;
    }

    public async Task<bool> CheckHealthAsync(CancellationToken token = default)
    {
        try
        {
            var response = await _client.GetAsync("/health", token);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<ModelInfo> GetModelInfoAsync(CancellationToken token = default)
    {
        try
        {
            var response = await _client.GetFromJsonAsync<JsonElement>("/model", token);

            var model = "Unknown";
            var provider = "Unknown";
            var available = false;

            if (response.TryGetProperty("model", out var modelObj))
            {
                if (modelObj.TryGetProperty("provider", out var p))
                    provider = p.GetString() ?? "Unknown";
                if (modelObj.TryGetProperty("model", out var m))
                    model = m.GetString() ?? "Unknown";
            }

            if (response.TryGetProperty("available", out var a))
                available = a.GetBoolean();

            return new ModelInfo(provider, model, available);
        }
        catch
        {
            return new ModelInfo("Offline", "N/A", false);
        }
    }

    public async Task<string> GetModeAsync(CancellationToken token = default)
    {
        try
        {
            var response = await _client.GetFromJsonAsync<JsonElement>("/mode", token);
            if (response.TryGetProperty("mode", out var mode))
                return mode.GetString() ?? "chat";
        }
        catch { }
        return "chat";
    }

    public async Task<string> SetModeAsync(string mode, CancellationToken token = default)
    {
        try
        {
            var request = new { mode };
            var response = await _client.PostAsJsonAsync("/mode", request, token);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<JsonElement>(token);
                if (result.TryGetProperty("mode", out var m))
                    return m.GetString() ?? mode;
            }
        }
        catch { }
        return mode;
    }

    public async Task<ProviderInfo> GetProviderAsync(CancellationToken token = default)
    {
        try
        {
            var response = await _client.GetFromJsonAsync<JsonElement>("/provider", token);

            var provider = "groq";
            var model = "unknown";
            var availableProviders = new List<string> { "groq", "ollama" };

            if (response.TryGetProperty("provider", out var p))
                provider = p.GetString() ?? "groq";
            if (response.TryGetProperty("model", out var m))
                model = m.GetString() ?? "unknown";
            if (response.TryGetProperty("available_providers", out var ap))
            {
                availableProviders.Clear();
                foreach (var item in ap.EnumerateArray())
                    availableProviders.Add(item.GetString() ?? "");
            }

            return new ProviderInfo(provider, model, availableProviders);
        }
        catch
        {
            return new ProviderInfo("groq", "unknown", new List<string> { "groq", "ollama" });
        }
    }

    public async Task<ProviderInfo> SetProviderAsync(string provider, string? model = null, CancellationToken token = default)
    {
        try
        {
            var request = new { provider, model };
            var response = await _client.PostAsJsonAsync("/provider", request, token);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<JsonElement>(token);
                var resultProvider = provider;
                var resultModel = model ?? "unknown";

                if (result.TryGetProperty("provider", out var p))
                    resultProvider = p.GetString() ?? provider;
                if (result.TryGetProperty("model", out var m))
                    resultModel = m.GetString() ?? model ?? "unknown";

                return new ProviderInfo(resultProvider, resultModel, new List<string> { "groq", "ollama" });
            }
        }
        catch { }
        return new ProviderInfo(provider, model ?? "unknown", new List<string> { "groq", "ollama" });
    }
}
