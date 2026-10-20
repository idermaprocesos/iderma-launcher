using Iderma.Core.Models;
using Iderma.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;

namespace Iderma_Launcher;

public sealed partial class SessionLockPage : Page
{
    public SessionLockPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        Frame.BackStack.Clear();
        TaskbarJumpList.Clear();
        App.Sessions.Changed += OnSessionsChanged;
        var user = Session.Current;
        if (user is null)
        {
            Frame.Navigate(typeof(LoginPage));
            return;
        }

        LockUserText.Text = $"{user.DisplayName}. La sesión sigue abierta. Escribe tu contraseña para continuar.";
        PasswordBox.Focus(FocusState.Programmatic);
        DispatcherQueue.TryEnqueue(() => _ = ExigirRedAsync());
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        App.Sessions.Changed -= OnSessionsChanged;
        base.OnNavigatedFrom(e);
    }

    private void OnSessionsChanged() => DispatcherQueue.TryEnqueue(CheckForcedLogout);

    private void CheckForcedLogout()
    {
        var user = Session.Current;
        if (user is null || !App.Sessions.ConsumeForcedLogout(user.Id))
        {
            return;
        }

        App.Security.Record(SecurityEventKind.ForcedSignOut, user.Username, "Cierre por un administrador.");
        AreaAppLauncher.CloseOpenedApps();
        Session.Notice = "Un administrador cerró tu sesión.";
        Session.Clear();
        Frame.Navigate(typeof(LoginPage));
    }

    private void PasswordBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            Unlock_Click(sender, e);
        }
    }

    private async void Unlock_Click(object sender, RoutedEventArgs e)
    {
        ErrorBar.IsOpen = false;
        if (!RedClinica.EstaConectada())
        {
            await RedClinica.AvisarYCerrarAsync(XamlRoot);
            return;
        }

        var user = Session.Current;
        if (user is null)
        {
            Frame.Navigate(typeof(LoginPage));
            return;
        }

        var password = PasswordBox.Password ?? string.Empty;
        if (string.IsNullOrWhiteSpace(password))
        {
            ShowError("Escribe tu contraseña.");
            return;
        }

        var username = user.Username;
        var (account, lockout) = await Task.Run<(UserAccount?, string?)>(() =>
        {
            var blocked = App.Security.LockoutMessage(username);
            if (blocked is not null)
            {
                return (null, blocked);
            }

            return (App.Users.Authenticate(username, password), null);
        });
        if (lockout is not null)
        {
            ShowError(lockout);
            return;
        }

        if (account is null)
        {
            PasswordBox.Password = string.Empty;
            var failure = await Task.Run(() => App.Security.RegisterFailure(username));
            ShowError(failure ?? "La contraseña no es correcta.");
            return;
        }

        if (account.MustChangePassword)
        {
            account = await CambioClave.ExigirAsync(account, XamlRoot);
            if (account is null)
            {
                return;
            }
        }

        Session.Current = account;
        App.Security.NoteUnlocked(account.Username);
        App.MainAppWindow?.NoteActivity();
        Frame.Navigate(typeof(HomePage));
    }

    private void Logout_Click(object sender, RoutedEventArgs e)
    {
        var username = Session.Current?.Username;
        App.Sessions.SignOut();
        Session.Clear();
        if (!string.IsNullOrWhiteSpace(username))
        {
            App.Security.Record(SecurityEventKind.SignOut, username, "Cierre de sesión desde la pantalla de bloqueo.");
        }

        Frame.Navigate(typeof(LoginPage));
    }

    private async Task ExigirRedAsync()
    {
        if (!RedClinica.EstaConectada())
        {
            await RedClinica.AvisarYCerrarAsync(XamlRoot);
        }
    }

    private void ShowError(string message)
    {
        ErrorBar.Title = "No se pudo continuar";
        ErrorBar.Message = message;
        ErrorBar.IsOpen = true;
    }
}
