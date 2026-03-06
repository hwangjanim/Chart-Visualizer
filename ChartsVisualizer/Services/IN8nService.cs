namespace ChartsVisualizer.Services;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public interface IN8nService
{
    Task<string> ProcessRequestAsync(string userPrompt, string currentCode, List<ChatMessage> history, string? dataSchema = null);
    IAsyncEnumerable<string> ProcessRequestStreamAsync(string userPrompt, string currentCode, List<ChatMessage> history, string? dataSchema = null, string? dbSchema = null, CancellationToken cancellationToken = default);
}

public class ChatMessage
{
    public string Role { get; set; } = "";
    public string Content { get; set; } = "";
    public string ThoughtProcess { get; set; } = "";
}
