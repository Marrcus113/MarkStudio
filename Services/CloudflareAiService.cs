using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MarkStudio.Models;

namespace MarkStudio.Services;

public class CloudflareAiService
{
    private readonly HttpClient _httpClient = new();
    private string _accountId = "";
    private string _apiToken = "";

    public bool IsConfigured => !string.IsNullOrEmpty(_accountId) && !string.IsNullOrEmpty(_apiToken);

    public void Configure(string accountId, string apiToken)
    {
        _accountId = accountId;
        _apiToken = apiToken;
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiToken);
    }

    public async Task<string> ChatAsync(string message, string model, List<ChatMessage>? history = null, CancellationToken ct = default)
    {
        if (!IsConfigured) throw new InvalidOperationException("API не настроен.");

        var messages = new List<object>();
        if (history != null)
        {
            foreach (var msg in history)
                messages.Add(new { role = msg.Role, content = msg.Content });
        }
        messages.Add(new { role = "user", content = message });

        var body = JsonSerializer.Serialize(new { model, messages, max_tokens = 2048, temperature = 0.3 });
        var url = $"https://api.cloudflare.com/client/v4/accounts/{_accountId}/ai/v1/chat/completions";
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(url, content, ct);
        var json = await response.Content.ReadAsStringAsync(ct);

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("result", out var result) &&
            result.TryGetProperty("choices", out var choices) &&
            choices.GetArrayLength() > 0)
        {
            return choices[0].GetProperty("message").GetProperty("content").GetString() ?? "";
        }

        throw new Exception($"Ошибка API: {json}");
    }

    public async IAsyncEnumerable<string> ChatStreamAsync(string message, string model, List<ChatMessage>? history = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!IsConfigured) throw new InvalidOperationException("API не настроен.");

        var messages = new List<object>();
        if (history != null)
        {
            foreach (var msg in history)
                messages.Add(new { role = msg.Role, content = msg.Content });
        }
        messages.Add(new { role = "user", content = message });

        var body = JsonSerializer.Serialize(new { model, messages, max_tokens = 2048, temperature = 0.3, stream = true });
        var url = $"https://api.cloudflare.com/client/v4/accounts/{_accountId}/ai/v1/chat/completions";

        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (true)
        {
            var line = await reader.ReadLineAsync();
            if (line == null) break;
            if (string.IsNullOrEmpty(line) || !line.StartsWith("data: ")) continue;

            var data = line[6..];
            if (data == "[DONE]") break;

            JsonDocument? doc = null;
            try { doc = JsonDocument.Parse(data); } catch { continue; }

            if (doc.RootElement.GetProperty("choices")[0].GetProperty("delta").TryGetProperty("content", out var token))
            {
                yield return token.GetString() ?? "";
            }
            doc.Dispose();
        }
    }
}

public class ChatMessage
{
    public string Role { get; set; } = "user";
    public string Content { get; set; } = "";
}
