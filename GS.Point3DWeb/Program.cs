using System.Globalization;
using GS.Point3DWeb.Components;
using GS.Point3DWeb.Localization;
using Microsoft.AspNetCore.Localization;
using GS.Point3DWeb.Services;
using GS.Point3DWeb.ViewModels;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Application-wide settings (connection list), shared like the desktop SettingsService singleton.
builder.Services.AddSingleton<SettingsService>();

// Feature key (free: 1 view, 5 servers; with a key: 4 views, any number of servers).
builder.Services.AddSingleton<FeatureKeyService>();

// Dialogs: one per browser connection (circuit).
builder.Services.AddScoped<DialogService>();

// Each browser connection's UI language, so renders triggered by other connections keep it.
builder.Services.AddScoped<CircuitCulture>();

// View models: transient so each view gets its own instance.
builder.Services.AddTransient<HomeViewModel>();
builder.Services.AddTransient<Model3DViewModel>();

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

// UI language: the culture cookie (set by /culture/set from the header language menu), else the browser's
// Accept-Language, else English. Only the UI culture changes; number and date formats stay the
// server's, so values sent to mounts and saved to JSON are never affected.
var serverCulture = CultureInfo.CurrentCulture;
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture(serverCulture, CultureInfo.GetCultureInfo(Languages.DefaultCode)),
    SupportedCultures = [serverCulture],
    SupportedUICultures = [.. Languages.All.Select(l => CultureInfo.GetCultureInfo(l.Code))]
});

app.UseAntiforgery();

app.MapStaticAssets();

// Header language menu: remember the choice for a year, then reload the page in it.
app.MapGet("/culture/set", (string? culture, string? redirectUri, HttpContext context) =>
{
    if (Languages.Find(culture) is { } language)
    {
        context.Response.Cookies.Append(
            CookieRequestCultureProvider.DefaultCookieName,
            // c= is not a supported culture, so the server's number formats stay; uic= picks the language.
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(language.Code)),
            new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true, SameSite = SameSiteMode.Lax, Path = "/" });
    }

    // Only redirect within this app.
    var target = redirectUri is { Length: > 0 } && redirectUri[0] == '/' && !redirectUri.StartsWith("//") && !redirectUri.StartsWith("/\\")
        ? redirectUri
        : "/";
    return Results.LocalRedirect(target);
});
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
