using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ChartsVisualizer.Models;

namespace ChartsVisualizer.Services;

public class N8nService : IN8nService
{
    private readonly HttpClient _httpClient;
    private readonly string _webhookUrl;

    public N8nService(HttpClient httpClient, IOptions<N8nSettings> settings)
    {
        _httpClient = httpClient;
        _webhookUrl = settings.Value.WebhookUrl
                      ?? throw new ArgumentNullException("N8n:WebhookUrl is not configured");
    }

    public async Task<string> ProcessRequestAsync(string userPrompt, string currentCode, List<ChatMessage> history, string? dataSchema = null)
    {
        var payload = new 
        { 
            chatInput = userPrompt,
            currentCode = currentCode,
            chatHistory = history,
            dataSchema = dataSchema ?? ""
        };
        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync(_webhookUrl, content);
        response.EnsureSuccessStatusCode();

        var responseString = await response.Content.ReadAsStringAsync();
        return ParseResponse(responseString);
    }

    public async IAsyncEnumerable<string> ProcessRequestStreamAsync(string userPrompt, string currentCode, List<ChatMessage> history, string? dataSchema = null, string? dbSchema = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var payload = new 
        { 
            chatInput = userPrompt,
            currentCode = currentCode,
            chatHistory = history,
            dataSchema = dataSchema ?? "",
            dbSchema = dbSchema ?? ""
        };
        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var request = new HttpRequestMessage(HttpMethod.Post, _webhookUrl) { Content = content };
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json")); // Fallback

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        var contentType = response.Content.Headers.ContentType?.MediaType;
        Console.WriteLine($"[N8n Webhook Response] Content-Type: {contentType}");

        // Read stream line-by-line. N8n might send genuine SSE ("data: {...}"),
        // or it might send NDJSON ("{...}\n{...}") if wrapped in application/json.
        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(line)) continue;

            Console.WriteLine($"[N8n Stream Line]: {line}");

            string dataChunk = line;
            if (line.StartsWith("data:"))
            {
                dataChunk = line.Substring("data:".Length).Trim();
                if (dataChunk == "[DONE]") break;
            }

            string? parsed = ParseStreamingChunk(dataChunk);
            if (!string.IsNullOrEmpty(parsed))
            {
                yield return parsed;
            }
        }
    }

    private string? ParseStreamingChunk(string dataChunk)
    {
        try 
        {
            using var doc = JsonDocument.Parse(dataChunk);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                // Inspect raw JSON inside stream
                Console.WriteLine($"[N8n parsed JSON]: {doc.RootElement.ToString()}");

                // Specifically handle LangChain event stream format if it exists
                if (doc.RootElement.TryGetProperty("type", out var typeEnum))
                {
                    string? typeStr = typeEnum.GetString();
                    if (typeStr == "item")
                    {
                        if (doc.RootElement.TryGetProperty("content", out var contentStr)) 
                            return contentStr.GetString();
                    }
                    else if (typeStr == "begin" || typeStr == "end")
                    {
                        if (doc.RootElement.TryGetProperty("metadata", out var metadata) && 
                            metadata.TryGetProperty("nodeName", out var nodeName))
                        {
                            string? name = nodeName.GetString();
                            if (!string.IsNullOrEmpty(name))
                            {
                                if (typeStr == "begin") return $"\n\n[⚙️ Started executing: {name}]...\n";
                                if (typeStr == "end") return $"\n[✅ Completed: {name}]\n";
                            }
                        }
                    }
                }

                if (doc.RootElement.TryGetProperty("token", out var token)) return token.GetString();
                if (doc.RootElement.TryGetProperty("text", out var text)) return text.GetString();
                if (doc.RootElement.TryGetProperty("log", out var log)) return log.GetString();
                if (doc.RootElement.TryGetProperty("action", out var action)) return $"\n[Action: {action.GetString()}]\n";
                if (doc.RootElement.TryGetProperty("tool", out var tool)) return $"\n[Using Tool: {tool.GetString()}]...\n";
                if (doc.RootElement.TryGetProperty("thought", out var thought)) return thought.GetString();
                
                // Do NOT return doc.RootElement.TryGetProperty("output").ToString() because output might be an object
                if (doc.RootElement.TryGetProperty("output", out var output))
                {
                    if (output.ValueKind == JsonValueKind.String) return output.GetString();
                }

                if (doc.RootElement.TryGetProperty("kwargs", out var kwargs) && 
                    kwargs.TryGetProperty("content", out var kwargContent)) 
                {
                    if (kwargContent.ValueKind == JsonValueKind.String) return kwargContent.GetString();
                }
                
                // If it's a JSON object but doesn't have our text fields, return null so we don't print raw JSON.
                return null;
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    if (element.ValueKind == JsonValueKind.Object)
                    {
                        if (element.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.String) return output.GetString();
                        if (element.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String) return text.GetString();
                        if (element.TryGetProperty("response", out var resp) && resp.ValueKind == JsonValueKind.String) return resp.GetString();
                        if (element.TryGetProperty("token", out var token) && token.ValueKind == JsonValueKind.String) return token.GetString();
                    }
                    else if (element.ValueKind == JsonValueKind.String)
                    {
                        return element.GetString();
                    }
                }
                return null;
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.String)
            {
                return doc.RootElement.GetString();
            }
        }
        catch (JsonException)
        {
            // If it's genuinely not JSON (e.g. plain text stream), return the raw data chunk
            return dataChunk;
        }

        return null;
    }

    private string ParseResponse(string responseString)
    {
        try 
        {
            using var doc = JsonDocument.Parse(responseString);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (doc.RootElement.TryGetProperty("output", out var output)) return output.ToString();
                if (doc.RootElement.TryGetProperty("text", out var text)) return text.ToString();
                if (doc.RootElement.TryGetProperty("response", out var resp)) return resp.ToString();
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    if (element.ValueKind == JsonValueKind.Object)
                    {
                        if (element.TryGetProperty("output", out var output)) return output.ToString();
                        if (element.TryGetProperty("text", out var text)) return text.ToString();
                        if (element.TryGetProperty("response", out var resp)) return resp.ToString();
                    }
                    else if (element.ValueKind == JsonValueKind.String)
                    {
                        return element.ToString();
                    }
                }
            }
        }
        catch (JsonException)
        {
            // If not JSON, return raw string
        }

        return responseString;
    }
}
