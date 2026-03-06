namespace ChartsVisualizer.Models;

public class N8nSettings
{
    public string WebhookUrl { get; set; } = string.Empty;
    public Dictionary<string, string> Workflows { get; set; } = new();
}
