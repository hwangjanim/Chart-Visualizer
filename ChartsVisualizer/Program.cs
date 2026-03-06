using ChartsVisualizer.Components;
using ChartsVisualizer.Models;
using ChartsVisualizer.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Bind N8n settings (Workflows dictionary)
builder.Services.Configure<N8nSettings>(
    builder.Configuration.GetSection("N8n"));

// Register HttpClient for N8nService and N8nTools
builder.Services.AddHttpClient<IN8nService, N8nService>();
builder.Services.AddHttpClient<N8nTools>();

builder.Services.AddSingleton<DatabaseService>();

// Register MCP server with the N8nTools
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Expose the MCP endpoint
app.MapMcp("/mcp");

app.Run();
