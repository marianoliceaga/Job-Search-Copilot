using JobPilot.Web.Components;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

// Local, single-user app: listen on the loopback interface only, never on external interfaces.
var port = builder.Configuration.GetValue("JobPilot:Port", 5080);
builder.WebHost.ConfigureKestrel(options => options.ListenLocalhost(port));

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddMudServices();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

await app.RunAsync();
