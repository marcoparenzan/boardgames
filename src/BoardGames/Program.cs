using BoardGames.Chess;
using BoardGames.Components;
using BoardGames.Core;
using BoardGames.Developy;

var builder = WebApplication.CreateBuilder(args);

// Optional settings file kept outside the repository (credentials such as the AzureAIFoundry section).
// Its path comes from --settings <path> or the BOARDGAMES_SETTINGS environment variable; nothing is hard-coded.
// When a path is given the file must exist. Environment variables and CLI arguments still override its values.
var settingsFile = builder.Configuration["settings"] ?? Environment.GetEnvironmentVariable("BOARDGAMES_SETTINGS");
if (!string.IsNullOrWhiteSpace(settingsFile))
{
    builder.Configuration.AddJsonFile(Path.GetFullPath(settingsFile), optional: false, reloadOnChange: true);
    builder.Configuration.AddEnvironmentVariables();
    builder.Configuration.AddCommandLine(args);
}

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Game modules: each registers its services and a GameDescriptor (lobby, navigation, routes, stylesheet).
builder.Configuration["Games:LogDirectory"] ??= Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "../../ops/data/games"));
builder.Services.AddChess();
builder.Services.AddDevelopy();

var app = builder.Build();

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
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(app.Services.GetServices<GameDescriptor>().Select(g => g.Assembly).Distinct().ToArray());

app.Run();
