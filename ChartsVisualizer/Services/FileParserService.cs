using ClosedXML.Excel;

namespace ChartsVisualizer.Services;

public static class FileParserService
{
    private const int SampleRowCount = 5;

    /// <summary>
    /// Parses a CSV or Excel file stream and returns a schema string 
    /// (column names + sample rows) suitable for an LLM prompt.
    /// </summary>
    public static async Task<string> ParseFileAsync(Stream fileStream, string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();

        // Buffer into MemoryStream — Blazor's SignalR stream doesn't support synchronous reads
        using var memoryStream = new MemoryStream();
        await fileStream.CopyToAsync(memoryStream);
        memoryStream.Position = 0;

        return extension switch
        {
            ".csv" => await ParseCsvAsync(memoryStream),
            ".xlsx" or ".xls" => ParseExcel(memoryStream),
            _ => throw new ArgumentException($"Unsupported file type: {extension}. Use .csv, .xlsx, or .xls")
        };
    }

    private static async Task<string> ParseCsvAsync(Stream stream)
    {
        using var reader = new StreamReader(stream);
        var lines = new List<string>();

        while (!reader.EndOfStream && lines.Count < SampleRowCount + 1) // +1 for header
        {
            var line = await reader.ReadLineAsync();
            if (!string.IsNullOrWhiteSpace(line))
                lines.Add(line);
        }

        if (lines.Count == 0)
            return "No data found in file.";

        var headers = lines[0].Split(',').Select(h => h.Trim().Trim('"')).ToArray();
        var sampleRows = lines.Skip(1).Select(line =>
        {
            var values = line.Split(',').Select(v => v.Trim().Trim('"')).ToArray();
            var row = new Dictionary<string, string>();
            for (int i = 0; i < Math.Min(headers.Length, values.Length); i++)
                row[headers[i]] = values[i];
            return row;
        }).ToList();

        return FormatSchema(headers, sampleRows);
    }

    private static string ParseExcel(Stream stream)
    {
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheets.First();
        var usedRange = worksheet.RangeUsed();

        if (usedRange == null)
            return "No data found in file.";

        var firstRow = usedRange.FirstRow().RowNumber();
        var lastRow = Math.Min(usedRange.LastRow().RowNumber(), firstRow + SampleRowCount);

        // Extract headers from first row
        var headers = usedRange.FirstRow().Cells()
            .Select(c => c.GetString().Trim())
            .ToArray();

        // Extract sample data rows
        var sampleRows = new List<Dictionary<string, string>>();
        for (int row = firstRow + 1; row <= lastRow; row++)
        {
            var rowData = new Dictionary<string, string>();
            for (int col = 0; col < headers.Length; col++)
            {
                var cell = worksheet.Cell(row, usedRange.FirstColumn().ColumnNumber() + col);
                rowData[headers[col]] = cell.GetString();
            }
            sampleRows.Add(rowData);
        }

        return FormatSchema(headers, sampleRows);
    }

    private static string FormatSchema(string[] headers, List<Dictionary<string, string>> sampleRows)
    {
        var sample = System.Text.Json.JsonSerializer.Serialize(sampleRows, 
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

        return $"""
            COLUMNS: {string.Join(", ", headers)}
            TOTAL SAMPLE ROWS: {sampleRows.Count}
            SAMPLE DATA (First {sampleRows.Count} rows):
            {sample}
            """;
    }
}
