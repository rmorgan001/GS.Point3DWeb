using CommunityToolkit.Mvvm.ComponentModel;

namespace GS.Point3DWeb.Models;

/// <summary>
/// An Alpaca server connection. Mirrors GS.Alpaca.Client.Models.AlpacaServer.
/// </summary>
public partial class AlpacaServer(string url, string name) : ObservableObject
{
    [ObservableProperty]
    public partial string IdGuid { get; set; } = Guid.NewGuid().ToString();

    [ObservableProperty]
    public partial bool IsConnected { get; set; }

    [ObservableProperty]
    public partial string ServerName { get; set; } = name;

    [ObservableProperty]
    public partial string Url { get; set; } = url;

    [ObservableProperty]
    public partial string? UserName { get; set; }

    [ObservableProperty]
    public partial string? Password { get; set; }

    [ObservableProperty]
    public partial double ClientTransactionId { get; set; }

    public void SetClientTransactionId(double value) => ClientTransactionId = value;
}
