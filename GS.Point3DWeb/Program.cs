using GS.Point3DWeb.Components;
using GS.Point3DWeb.Services;
using GS.Point3DWeb.ViewModels;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Application-wide settings (connection list), shared like the desktop SettingsService singleton.
builder.Services.AddSingleton<SettingsService>();

// Dialogs: one per browser connection (circuit).
builder.Services.AddScoped<DialogService>();

// View models: transient so each view gets its own instance.
builder.Services.AddTransient<HomeViewModel>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
