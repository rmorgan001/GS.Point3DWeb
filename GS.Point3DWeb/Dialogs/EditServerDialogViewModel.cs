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
    private static readonly Regex TelescopePath =
        new(@"/api/v1/telescope/\d+/$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public EditServerDialogViewModel(AlpacaServer server)
    {
        IdGuid = server.IdGuid;
        ServerName = server.ServerName;
        Url = server.Url;
        UserName = server.UserName;
        Password = server.Password;
        PollSeconds = server.PollSeconds;
        ClientTransactionId = server.ClientTransactionId;
    }

    [ObservableProperty] public partial string Title { get; set; } = L.T("Edit Server");
    [ObservableProperty] public partial string AcceptText { get; set; } = L.T("Accept");
    [ObservableProperty] public partial string CancelText { get; set; } = L.T("Cancel");

    public string IdGuid { get; }
    public double ClientTransactionId { get; }

    [ObservableProperty] public partial string ServerName { get; set; }
    [ObservableProperty] public partial string Url { get; set; }
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

    /// <summary>Copies the edited values onto the real server.</summary>
    public void ApplyTo(AlpacaServer server)
    {
        var urlChanged = !string.Equals(server.Url, Url.Trim(), StringComparison.OrdinalIgnoreCase);

        server.ServerName = ServerName.Trim();
        server.Url = Url.Trim();
        server.UserName = string.IsNullOrWhiteSpace(UserName) ? null : UserName;
        server.Password = string.IsNullOrEmpty(Password) ? null : Password;
        server.PollSeconds = AlpacaServer.ClampPollSeconds(PollSeconds);

        // A different address is a different device, so the old connection no longer applies.
        if (urlChanged) server.IsConnected = false;
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

        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
            !(uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            UrlError = L.T("Invalid");
            return;
        }

        UrlError = TelescopePath.IsMatch(uri.AbsolutePath)
            ? null
            : L.T("Invalid path (expected …/api/v1/telescope/{n}/)");
    }
}
