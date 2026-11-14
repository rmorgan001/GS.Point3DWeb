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

using GS.Point3DWeb.Localization;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GS.Point3DWeb.Models;

namespace GS.Point3DWeb.Dialogs;

/// <summary>
/// Edits one server's fields (the field area under the grid in GS.Alpaca.Client's ConnectPageView).
/// Works on a copy; <see cref="ApplyTo"/> writes the values back only after Accept.
/// Validation follows GS.Alpaca.Client.Models.AlpacaServer.
/// </summary>
public partial class EditServerDialogViewModel : DialogViewModel
{
    // The trailing slash is optional: it is added when the server is saved (AlpacaServer.NormalizeUrl).
    private static readonly Regex TelescopePath =
        new(@"/api/v1/telescope/\d+/?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // A COM ProgID: letters, digits, '_' and '.', starting with a letter (e.g. ASCOM.Simulator.Telescope).
    private static readonly Regex ComProgId = new(@"^[A-Za-z][A-Za-z0-9_.]*$", RegexOptions.Compiled);

    /// <param name="server">The server to edit.</param>
    /// <param name="servers">The whole list, so an ASCOM driver another entry already uses is refused.</param>
    public EditServerDialogViewModel(AlpacaServer server, IReadOnlyList<AlpacaServer>? servers = null)
    {
        _others = servers?.Where(s => s != server).ToArray() ?? [];
        IdGuid = server.IdGuid;
        ServerName = server.ServerName;
        Url = server.Url;
        UserName = server.UserName;
        Password = server.Password;
        PollSeconds = server.PollSeconds;
        ClientTransactionId = server.ClientTransactionId;
        _passwordEdited = false; // setting Password above isn't an edit
    }

    // The other servers in the list (an ASCOM driver may be in it once).
    private readonly AlpacaServer[] _others;

    // Whether the user typed in (or cleared) the password field.
    private bool _passwordEdited;

    partial void OnPasswordChanged(string? value) => _passwordEdited = true;

    [ObservableProperty] public partial string Title { get; set; } = L.T("Edit Server");
    [ObservableProperty] public partial string AcceptText { get; set; } = L.T("Accept");
    [ObservableProperty] public partial string CancelText { get; set; } = L.T("Cancel");

    public string IdGuid { get; }
    public double ClientTransactionId { get; }

    [ObservableProperty] public partial string ServerName { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCom))]
    public partial string Url { get; set; }

    /// <summary>The address is an ASCOM COM driver, which has no user name or password.</summary>
    public bool IsCom => AlpacaServer.IsComUrl(Url ?? "");
    [ObservableProperty] public partial string? UserName { get; set; }
    [ObservableProperty] public partial string? Password { get; set; }

    /// <summary>How often the server is asked for its state, in seconds (0.5 to 5.0).</summary>
    [ObservableProperty] public partial double PollSeconds { get; set; }

    [ObservableProperty] public partial string? PollSecondsError { get; set; }

    [ObservableProperty] public partial string? ServerNameError { get; set; }
    [ObservableProperty] public partial string? UrlError { get; set; }

    [ObservableProperty] public partial bool Confirmed { get; set; }

    public bool HasErrors => ServerNameError is not null || UrlError is not null || PollSecondsError is not null;

    partial void OnPollSecondsChanged(double value) => ValidatePollSeconds(value);

    private void ValidatePollSeconds(double value)
        => PollSecondsError = double.IsNaN(value) || value < AlpacaServer.MinPollSeconds || value > AlpacaServer.MaxPollSeconds
            ? L.T("{0} to {1} seconds", AlpacaServer.MinPollSeconds.ToString("0.0"), AlpacaServer.MaxPollSeconds.ToString("0.0"))
            : null;

    partial void OnServerNameChanged(string value) => ValidateServerName(value);

    partial void OnUrlChanged(string value) => ValidateUrl(value);

    /// <summary>Copies the edited values onto the real server. Returns true if its address changed.</summary>
    public bool ApplyTo(AlpacaServer server)
    {
        var url = AlpacaServer.NormalizeUrl(Url);
        var urlChanged = !AlpacaServer.SameUrl(server.Url, url);

        server.ServerName = ServerName.Trim();
        server.Url = url;
        if (AlpacaServer.IsComUrl(url))
        {
            // A COM driver has no credentials: the fields are hidden, so don't keep any it can't show.
            server.UserName = null;
            server.Password = null;
            server.PasswordUnreadable = false;
        }
        else
        {
            server.UserName = string.IsNullOrWhiteSpace(UserName) ? null : UserName;
            server.Password = string.IsNullOrEmpty(Password) ? null : Password;
            // A saved password that couldn't be decrypted is replaced (or removed) only when the user edits it.
            if (_passwordEdited) server.PasswordUnreadable = false;
        }
        server.PollSeconds = AlpacaServer.ClampPollSeconds(PollSeconds);

        // A different address is a different device, so the old connection no longer applies.
        if (urlChanged)
        {
            server.ConnectRequested = false;
            server.IsConnected = false;
        }
        return urlChanged;
    }

    [RelayCommand]
    private Task ConfirmAsync()
    {
        ValidateServerName(ServerName);
        ValidateUrl(Url);
        ValidatePollSeconds(PollSeconds);
        if (HasErrors) return Task.CompletedTask;

        Confirmed = true;
        Close();
        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task CancelAsync()
    {
        Confirmed = false;
        Close();
        return Task.CompletedTask;
    }

    private void ValidateServerName(string? value)
        => ServerNameError = string.IsNullOrWhiteSpace(value) ? L.T("Required") : null;

    private void ValidateUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            UrlError = L.T("Required");
            return;
        }

        // An ASCOM COM driver on this computer: ascom:<ProgID>, e.g. ascom:ASCOM.Simulator.Telescope
        if (AlpacaServer.IsComUrl(value))
        {
            var progId = AlpacaServer.GetProgId(value)!;
            // One entry per driver: the driver is shared, so a second entry would only confuse its state.
            var duplicate = _others.FirstOrDefault(s => AlpacaServer.SameUrl(s.Url, value));
            UrlError = !Services.ComTelescopes.IsSupported ? Services.AlpacaConnector.ComNotSupportedMessage
                : !ComProgId.IsMatch(progId) ? L.T("Invalid ProgID (expected ascom:<ProgID>)")
                : duplicate is not null ? L.T("Already in the list as {0}", duplicate.ServerName)
                : null;
            return;
        }

        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
            !(uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            UrlError = L.T("Invalid");
            return;
        }

        // The address must end with the device path: requests are built by adding to it, so a query (?…) or
        // fragment (#…) after the path would break every one of them.
        UrlError = TelescopePath.IsMatch(uri.AbsolutePath) && uri.Query.Length == 0 && uri.Fragment.Length == 0
            ? null
            : L.T("Invalid path (expected …/api/v1/telescope/{n}/)");
    }
}
