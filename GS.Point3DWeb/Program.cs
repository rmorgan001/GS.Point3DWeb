using System.Globalization;
using GS.Point3DWeb.Components;
using GS.Point3DWeb.Localization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Localization;
using GS.Point3DWeb.Services;
using GS.Point3DWeb.Support;
using GS.Point3DWeb.ViewModels;
using Serilog;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    // A published copy is often started from another folder (a shortcut, a service, ./folder/GS.Point3DWeb), and
    // the content root defaults to the current folder: use the app's own folder, where appsettings.json is.
    // Development (dotnet run, Visual Studio) keeps the project folder.
    ContentRootPath = IsDevelopment() ? null : AppContext.BaseDirectory
});

static bool IsDevelopment()
    => string.Equals(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                     ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"),
                     "Development", StringComparison.OrdinalIgnoreCase);

// Logging: Serilog writes to the console and to a daily file in AppLog.Folder (Documents\GS.Point3DWeb on Windows; two weeks
// kept), with the levels from the "Serilog" section of appsettings.json. The Debug output and, on Windows,
// the Event Log (warnings and errors) still get the messages too. Serilog hands messages to those providers
// directly, past the usual "Logging" filter rules, so the Event Log's level is set on the provider itself.
builder.Logging.ClearProviders();
builder.Logging.AddDebug();
if (OperatingSystem.IsWindows())
    builder.Logging.AddEventLog(AppLog.EventLogWarningsAndErrorsOnly);

builder.Services.AddSerilog((services, log) => log
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
        .WriteTo.File(Path.Combine(AppLog.Folder, "point3dweb-.log"),
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 14,
            fileSizeLimitBytes: 20 * 1024 * 1024,
            rollOnFileSizeLimit: true,
            shared: true,
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}"),
    writeToProviders: true);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Data Protection encrypts the saved server passwords. A fixed application name keeps its keys usable
// wherever the app runs from (by default they are tied to the content-root path).
builder.Services.AddDataProtection().SetApplicationName("GS.Point3DWeb");

// Application-wide settings (connection list), shared like the desktop SettingsService singleton.
builder.Services.AddSingleton<SettingsService>();

// Feature key (free: all 4 views and 5 servers; with a key: any number of servers).
builder.Services.AddSingleton<FeatureKeyService>();

// Dialogs: one per browser connection (circuit).
builder.Services.AddScoped<DialogService>();

// Each browser connection's UI language, so renders triggered by other connections keep it.
builder.Services.AddScoped<CircuitCulture>();

// Whether each browser connection can be reached: the 3D views stop polling while it can't.
builder.Services.AddScoped<CircuitState>();
builder.Services.AddScoped<Microsoft.AspNetCore.Components.Server.Circuits.CircuitHandler>(sp => sp.GetRequiredService<CircuitState>());

// View models: transient so each view gets its own instance.
builder.Services.AddTransient<HomeViewModel>();
builder.Services.AddTransient<Model3DViewModel>();

var app = builder.Build();

// The static connection classes (AlpacaConnector, ComTelescopes) log through this.
AppLog.Factory = app.Services.GetRequiredService<ILoggerFactory>();
app.Logger.LogInformation("GS Point3D Web {Version} starting; logs in {LogFolder}", AppVersion.Display, AppLog.Folder);
if (AppLog.FolderProblem is { } logFolderProblem)
    app.Logger.LogWarning("Log folder problem: {Problem}", logFolderProblem);

// Release the ASCOM COM drivers this app connected, so drivers that count their clients don't stay
// connected for it after it stops.
if (ComTelescopes.IsSupported)
    app.Lifetime.ApplicationStopping.Register(ComTelescopes.DisconnectAll);

// Only Windows encrypts the Data Protection keys at rest (DPAPI). Elsewhere they are plain files in the
// user's profile (~/.aspnet/DataProtection-Keys), or kept in memory if there is no profile, so say so.
if (!OperatingSystem.IsWindows())
{
    app.Logger.LogWarning(
        "Saved server passwords are encrypted with keys that are not themselves encrypted on this system; " +
        "anyone who can read this user's files can read the passwords. In a container, mount the user's home " +
        "folder or the keys (and so the passwords) are lost on every restart.");
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
// Redirect to https only when there is somewhere to go: an https address the server is actually listening on
// (known once it has started, so looked at on the first request rather than guessed from the configuration) or
// an https port set for a reverse proxy that ends TLS (in code, with AddHttpsRedirection, or in the configuration,
// where the middleware also looks). With only http:// addresses (the default) the middleware has nowhere to
// redirect and would warn on every start; any warning it gives otherwise is a real problem and stays in the log.
var hasHttpsTarget = new Lazy<bool>(() =>
    app.Services.GetService<IOptions<HttpsRedirectionOptions>>()?.Value.HttpsPort is not null
    || !string.IsNullOrEmpty(app.Configuration["HTTPS_PORT"] ?? app.Configuration["ANCM_HTTPS_PORT"] ?? app.Configuration["HTTPS_PORTS"])
    || (app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses
            .Any(a => a.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) ?? false));
app.UseWhen(_ => hasHttpsTarget.Value, https => https.UseHttpsRedirection());

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

    // Only redirect within this app. Control characters are refused too: LocalRedirect would refuse them by
    // throwing, which is an error page instead of the home page.
    var target = redirectUri is { Length: > 0 } && redirectUri[0] == '/' && !redirectUri.StartsWith("//") && !redirectUri.StartsWith("/\\")
                 && !redirectUri.Any(char.IsControl)
        ? redirectUri
        : "/";
    return Results.LocalRedirect(target);
});
// The log viewer's Download link: the most recent log file, read while it is still being written.
app.MapGet("/log/file", () => LogFiles.Latest() is { } file
    ? Results.File(LogFiles.OpenShared(file), "text/plain; charset=utf-8", file.Name)
    : Results.NotFound());

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Started by hand (the exe, a shortcut): open the app in the browser once the server is listening (see
// BrowserLauncher.ShouldOpen for when it doesn't, and how to turn it off).
if (BrowserLauncher.ShouldOpen(app.Configuration, app.Environment))
{
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses ?? [];
        if (BrowserLauncher.PickUrl(addresses) is { } url) BrowserLauncher.Open(url, app.Logger);
    });
}

app.Run();
