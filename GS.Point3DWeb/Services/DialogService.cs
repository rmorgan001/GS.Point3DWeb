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

    public async Task ShowDialog(DialogViewModel dialogViewModel)
    {
        Current = dialogViewModel;
        dialogViewModel.Show();
        DialogChanged?.Invoke();

        try
        {
            await dialogViewModel.WaitAsync();
        }
        finally
        {
            if (ReferenceEquals(Current, dialogViewModel))
            {
                Current = null;
                DialogChanged?.Invoke();
            }
        }
    }
}
