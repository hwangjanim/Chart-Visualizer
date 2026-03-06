using Microsoft.Data.Sqlite;
using System.Data;

namespace ChartsVisualizer.Services;

public class DatabaseService
{
    private readonly string _connectionString;

    public DatabaseService(IConfiguration configuration)
    {
        var dbPath = configuration["Database:Path"] ?? "charts.db";
        _connectionString = $"Data Source={dbPath}";
        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS Sales (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Region TEXT,
                Category TEXT,
                Amount REAL,
                SaleDate DATE
            );

            -- Only insert if empty
            INSERT INTO Sales (Region, Category, Amount, SaleDate) 
            SELECT 'North', 'Electronics', 1200.50, '2024-01-15' WHERE NOT EXISTS (SELECT 1 FROM Sales);
            INSERT INTO Sales (Region, Category, Amount, SaleDate) 
            SELECT 'North', 'Clothing', 450.00, '2024-01-16' WHERE NOT EXISTS (SELECT 1 FROM Sales LIMIT 1 OFFSET 1);
            INSERT INTO Sales (Region, Category, Amount, SaleDate) 
            SELECT 'South', 'Electronics', 850.25, '2024-01-15' WHERE NOT EXISTS (SELECT 1 FROM Sales LIMIT 1 OFFSET 2);
            INSERT INTO Sales (Region, Category, Amount, SaleDate) 
            SELECT 'South', 'Clothing', 700.00, '2024-01-17' WHERE NOT EXISTS (SELECT 1 FROM Sales LIMIT 1 OFFSET 3);
            INSERT INTO Sales (Region, Category, Amount, SaleDate) 
            SELECT 'East', 'Electronics', 1500.00, '2024-01-18' WHERE NOT EXISTS (SELECT 1 FROM Sales LIMIT 1 OFFSET 4);

            CREATE TABLE IF NOT EXISTS Products (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT,
                StockQuantity INTEGER,
                UnitPrice REAL
            );

            INSERT INTO Products (Name, StockQuantity, UnitPrice)
            SELECT 'Laptop', 50, 999.99 WHERE NOT EXISTS (SELECT 1 FROM Products);
            INSERT INTO Products (Name, StockQuantity, UnitPrice)
            SELECT 'Smartphone', 150, 599.49 WHERE NOT EXISTS (SELECT 1 FROM Products LIMIT 1 OFFSET 1);
        ";
        command.ExecuteNonQuery();
    }

    private const int SampleRowCount = 5;

    public async Task<string> GetSchemaAsync()
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var schema = new System.Text.StringBuilder();
        schema.AppendLine("DATABASE SCHEMA (SQLite):");

        var tables = new List<string>();
        var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'";
        using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                tables.Add(reader.GetString(0));
            }
        }

        foreach (var table in tables)
        {
            schema.AppendLine($"Table: {table}");

            // Get column info
            command.CommandText = $"PRAGMA table_info({table})";
            var colNames = new List<string>();
            using (var reader = await command.ExecuteReaderAsync())
            {
                schema.Append("  Columns: ");
                var cols = new List<string>();
                while (await reader.ReadAsync())
                {
                    colNames.Add(reader.GetString(1));
                    cols.Add($"{reader.GetString(1)} ({reader.GetString(2)})");
                }
                schema.AppendLine(string.Join(", ", cols));
            }

            // Get sample rows
            command.CommandText = $"SELECT * FROM \"{table}\" LIMIT {SampleRowCount}";
            var sampleRows = new List<Dictionary<string, object>>();
            using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    var row = new Dictionary<string, object>();
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        row[reader.GetName(i)] = reader.IsDBNull(i) ? "(null)" : reader.GetValue(i);
                    }
                    sampleRows.Add(row);
                }
            }

            var sampleJson = System.Text.Json.JsonSerializer.Serialize(sampleRows,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            schema.AppendLine($"  TOTAL SAMPLE ROWS: {sampleRows.Count}");
            schema.AppendLine($"  SAMPLE DATA (First {sampleRows.Count} rows):");
            schema.AppendLine(sampleJson);
        }

        return schema.ToString();
    }

    public async Task<List<Dictionary<string, object>>> ExecuteQueryAsync(string sql)
    {
        var results = new List<Dictionary<string, object>>();
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = sql;

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var row = new Dictionary<string, object>();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                row[reader.GetName(i)] = reader.GetValue(i);
            }
            results.Add(row);
        }

        return results;
    }
}
