using Iderma.Core.Models;
using Iderma.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Iderma_Launcher;

internal static class CambioClave
{
    public static async Task<UserAccount?> ExigirAsync(UserAccount user, XamlRoot root)
    {
        if (!user.MustChangePassword)
        {
            return user;
        }

        var aviso = "Esta contraseña ya no es válida. Elige una nueva de al menos 10 caracteres, con una letra y un número.";
        while (true)
        {
            var actual = new PasswordBox { PlaceholderText = "Contraseña actual" };
            var nueva = new PasswordBox { PlaceholderText = "Nueva contraseña" };
            var repetir = new PasswordBox { PlaceholderText = "Repite la nueva contraseña" };
            var dialog = new ContentDialog
            {
                Title = "Cambia tu contraseña",
                Content = new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock { Text = aviso, TextWrapping = TextWrapping.Wrap },
                        actual,
                        nueva,
                        repetir
                    }
                },
                PrimaryButtonText = "Guardar",
                CloseButtonText = "Cancelar",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = root
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return null;
            }

            if (!string.Equals(nueva.Password, repetir.Password, StringComparison.Ordinal))
            {
                aviso = "La nueva contraseña y la repetición no coinciden.";
                continue;
            }

            try
            {
                return App.Users.ChangePassword(user.Id, actual.Password, nueva.Password);
            }
            catch (Exception ex)
            {
                aviso = ex.Message;
            }
        }
    }
}
