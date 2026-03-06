using System.ComponentModel;
using Microsoft.Extensions.Options;
using ChartsVisualizer.Models;
using ModelContextProtocol.Server;

namespace ChartsVisualizer.Services;

[McpServerToolType]
public class N8nTools
{
    private readonly N8nSettings _settings;
    private readonly HttpClient _httpClient;
    private readonly DatabaseService _dbService;

    public N8nTools(IOptions<N8nSettings> settings, HttpClient httpClient, DatabaseService dbService)
    {
        _settings = settings.Value;
        _httpClient = httpClient;
        _dbService = dbService;
    }

    [McpServerTool] [Description("Generate a chart image based on a data summary. Use this when the user asks for a visual representation.")]
    public async Task<string> GenerateChart(string dataSummary)
    {
        var url = _settings.Workflows["Charting"];
        var schema = await _dbService.GetSchemaAsync();
        
        var payload = new 
        { 
            chatInput = dataSummary, // The charting agent expects "chatInput"
            dbSchema = schema
        };
        
        var response = await _httpClient.PostAsJsonAsync(url, payload);
        return await response.Content.ReadAsStringAsync();
    }

    [McpServerTool]
    [Description("Passes the user's data question to the SQL Expert Agent to generate a SQL query, executes it locally, and returns the raw data results.")]
    public async Task<string> ExecuteSql(string userQuestion)
    {
        var url = _settings.Workflows["SqlWriter"];
        var schema = await _dbService.GetSchemaAsync();
        
        var payload = new 
        { 
            chatInput = userQuestion,
            dbSchema = schema
        };
        
        var response = await _httpClient.PostAsJsonAsync(url, payload);
        var responseString = await response.Content.ReadAsStringAsync();

        // 1. Extract the text from the n8n webhook response (handles streaming JSON lines and standard JSON)
        string sqlReply = "";
        try
        {
            var fullContent = new System.Text.StringBuilder();
            var lines = responseString.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(line);
                    if (doc.RootElement.TryGetProperty("output", out var outputEl))
                    {
                        fullContent.Append(outputEl.GetString());
                    }
                    else if (doc.RootElement.TryGetProperty("type", out var typeEl) && typeEl.GetString() == "item" && doc.RootElement.TryGetProperty("content", out var contentEl))
                    {
                        fullContent.Append(contentEl.GetString());
                    }
                }
                catch { }
            }
            sqlReply = fullContent.Length > 0 ? fullContent.ToString() : responseString;
        }
        catch 
        { 
            sqlReply = responseString; 
        }

        // 2. Look for the SQL query inside ```sql ... ``` block
        var match = System.Text.RegularExpressions.Regex.Match(
            sqlReply, 
            @"```sql\s*(.*?)\s*```", 
            System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            
        string sqlQuery = match.Success ? match.Groups[1].Value.Trim() : sqlReply.Trim();

        // 3. Execute the query locally against the actual SQLite database
        try
        {
            var results = await _dbService.ExecuteQueryAsync(sqlQuery);
            return System.Text.Json.JsonSerializer.Serialize(results, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return $"Failed to execute SQL query: {ex.Message}\nQuery attempted:\n{sqlQuery}";
        }
    }

}