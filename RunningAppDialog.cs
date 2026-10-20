using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Iderma_Launcher;

internal static class RunningAppDialog
{
    public static async Task ShowAsync(XamlRoot? root, string appId, string name)
    {
        if (root is null)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = name,
            Content = "La aplicación ya se está ejecutando.",
            PrimaryButtonText = "Forzar cierre",
            SecondaryButtonText = "Reiniciar launcher",
            DefaultButton = ContentDialogButton.None,
            XamlRoot = root
        };

        ContentDialogResult result;
        try
        {
            result = await dialog.ShowAsync();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            return;
        }

        if (result == ContentDialogResult.Primary)
        {
            AreaAppLauncher.ForceClose(appId);
            return;
        }

        if (result == ContentDialogResult.Secondary)
        {
            AreaAppLauncher.RestartLauncher();
        }
    }
}
