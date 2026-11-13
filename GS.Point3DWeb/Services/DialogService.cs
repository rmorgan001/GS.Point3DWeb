using GS.Point3DWeb.Dialogs;

namespace GS.Point3DWeb.Services;

/// <summary>
/// Shows dialogs for one browser connection (registered as scoped, so one per circuit).
/// View models call <see cref="ShowDialog"/> and await it; the DialogHost component renders
/// <see cref="Current"/>. Mirrors GS.Alpaca.Client.Singletons.DialogService.ShowDialog.
/// </summary>
public class DialogService
{
    public DialogViewModel? Current { get; private set; }

    public event Action? DialogChanged;

    // One dialog at a time: a dialog asked for while another is open (say, a failed connect while the user
    // is editing a server) waits its turn. Replacing the open one would leave whoever is waiting on it
    // waiting for ever.
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);

    /// <summary>Shows the dialog once any open one has closed, and completes when it closes.</summary>
    public async Task ShowDialog(DialogViewModel dialogViewModel)
    {
        await _oneAtATime.WaitAsync();
        try
        {
            Current = dialogViewModel;
            dialogViewModel.Show();
            DialogChanged?.Invoke();
            await dialogViewModel.WaitAsync();
        }
        finally
        {
            Current = null;
            DialogChanged?.Invoke();
            _oneAtATime.Release();
        }
    }
}
