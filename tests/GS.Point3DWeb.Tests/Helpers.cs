/* Copyright(C) 2026  Rob Morgan (robert.morgan.e@gmail.com)

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published
    by the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <https://www.gnu.org/licenses/>.
 */

using System.Globalization;
using System.Runtime.CompilerServices;
using GS.Point3DWeb.Dialogs;
using GS.Point3DWeb.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;

[assembly: CollectionBehavior(MaxParallelThreads = 8)]

namespace GS.Point3DWeb.Tests;

internal static class TestSetup
{
    /// <summary>Every test sees the UI in English, whatever the machine's language.</summary>
    [ModuleInitializer]
    internal static void English()
    {
        var en = CultureInfo.GetCultureInfo("en");
        CultureInfo.DefaultThreadCurrentUICulture = en;
        CultureInfo.CurrentUICulture = en;
    }
}

/// <summary>Paths in the repository (the tests run from bin/…).</summary>
internal static class Repo
{
    public static string Root { get; } = Find();

    public static string WebProject => Path.Combine(Root, "GS.Point3DWeb");
    public static string WebRoot => Path.Combine(WebProject, "wwwroot");

    private static string Find()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "GS.Point3DWeb.slnx"))) return dir.FullName;
        }
        throw new InvalidOperationException("Repository root not found above " + AppContext.BaseDirectory);
    }
}

/// <summary>
/// SettingsService and FeatureKeyService always use the user's application-data folder. These tests only
/// run where that folder is a throw-away one: on Linux / macOS with XDG_CONFIG_HOME pointing at a test
/// folder (see tests/run-tests.sh). On Windows it is the real %APPDATA%, so they are skipped there.
/// </summary>
internal static class Sandbox
{
    public static bool IsIsolated { get; } = Check();

    public static string AppFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GS.Point3DWeb");

    public static string SkipReason =>
        "Needs an isolated settings folder: run on Linux with XDG_CONFIG_HOME set to a folder containing 'gsp3w-test' (tests/run-tests.sh).";

    private static bool Check()
    {
        if (OperatingSystem.IsWindows()) return false;
        var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrEmpty(xdg) || !xdg.Contains("gsp3w-test", StringComparison.Ordinal)) return false;
        return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData).Contains("gsp3w-test", StringComparison.Ordinal);
    }

    /// <summary>Empties the sandbox's settings folder.</summary>
    public static void Reset()
    {
        if (!IsIsolated) throw new InvalidOperationException("Not isolated");
        var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        foreach (var dir in new[] { AppFolder })
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        Directory.CreateDirectory(root);
    }

    public static readonly IDataProtectionProvider Protection = new EphemeralDataProtectionProvider();

    public static SettingsService NewSettings(IDataProtectionProvider? protection = null)
        => new(NullLogger<SettingsService>.Instance, protection ?? Protection);

    public static FeatureKeyService NewFeatureKeys() => new(NullLogger<FeatureKeyService>.Instance);
}

/// <summary>A fact that only runs with an isolated settings folder (see <see cref="Sandbox"/>).</summary>
public sealed class IsolatedFactAttribute : FactAttribute
{
    public IsolatedFactAttribute()
    {
        if (!Sandbox.IsIsolated) Skip = Sandbox.SkipReason;
    }
}

[CollectionDefinition("Settings", DisableParallelization = true)]
public sealed class SettingsCollection;

/// <summary>Tests that change a shared (static) timing, so they run on their own.</summary>
[CollectionDefinition("Serial", DisableParallelization = true)]
public sealed class SerialCollection;

/// <summary>
/// A SettingsService that never touches the disk, for view models that only read a setting or two from it
/// (Model3DViewModel reads ModelSecondaryColor). Its constructor isn't run, so it doesn't load the real file.
/// </summary>
internal static class DiskFreeSettings
{
    public static SettingsService Create() => (SettingsService)RuntimeHelpers.GetUninitializedObject(typeof(SettingsService));
}

/// <summary>Answers every dialog a DialogService shows, as a user would, and keeps a record of them.</summary>
internal sealed class DialogResponder
{
    private readonly DialogService _dialogs;
    public List<DialogViewModel> Shown { get; } = [];

    /// <summary>What to do with a dialog: true accepts it, false cancels it. Default: accept.</summary>
    public Func<DialogViewModel, bool> Decide { get; set; } = _ => true;

    /// <summary>Called on an edit dialog before it is accepted, to type into it.</summary>
    public Action<EditServerDialogViewModel>? Edit { get; set; }

    public DialogResponder(DialogService dialogs)
    {
        _dialogs = dialogs;
        dialogs.DialogChanged += OnChanged;
    }

    public IEnumerable<string> Messages => Shown.OfType<ConfirmDialogViewModel>().Select(c => c.Message);

    private void OnChanged()
    {
        if (_dialogs.Current is not { IsDialogOpen: true } dialog) return;
        lock (Shown) Shown.Add(dialog);
        var accept = Decide(dialog);
        switch (dialog)
        {
            case ConfirmDialogViewModel confirm:
                if (accept) confirm.ConfirmCommand.Execute(null); else confirm.CancelCommand.Execute(null);
                break;
            case EditServerDialogViewModel edit:
                if (accept) { Edit?.Invoke(edit); edit.ConfirmCommand.Execute(null); }
                else edit.CancelCommand.Execute(null);
                if (edit.IsDialogOpen) edit.CancelCommand.Execute(null); // invalid input: give up
                break;
            default:
                dialog.Close();
                break;
        }
    }
}

internal static class Wait
{
    /// <summary>Waits until the condition holds, or fails the test after the timeout.</summary>
    public static async Task UntilAsync(Func<bool> condition, TimeSpan timeout, string what)
    {
        var until = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > until) Assert.Fail($"Timed out after {timeout.TotalSeconds:0.#} s waiting for: {what}");
            await Task.Delay(25);
        }
    }
}
