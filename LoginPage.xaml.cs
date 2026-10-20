using System.IO;
using Iderma.Core.Models;
using Iderma.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;

namespace Iderma_Launcher;

public sealed partial class LoginPage : Page
{
    private LoginSlideshow? _slideshow;

    public LoginPage()
    {
        InitializeComponent();
        ApplyBrandLogo();
        _slideshow = new LoginSlideshow(SlideA, SlideB);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        Frame.BackStack.Clear();
        App.MainAppWindow?.SetHeaderTitle(Iderma.Core.AppInfo.ProductName);
        _slideshow ??= new LoginSlideshow(SlideA, SlideB);
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, StartDeferredChrome);

        if (!string.IsNullOrWhiteSpace(Session.Notice))
        {
            ErrorBar.Severity = InfoBarSeverity.Informational;
            ErrorBar.Title = "Sesión cerrada";
            ErrorBar.Message = Session.Notice;
            ErrorBar.IsOpen = true;
            Session.Notice = null;
        }
        else if (File.Exists(UserStore.AvisoClaveInicial))
        {
            ErrorBar.Severity = InfoBarSeverity.Informational;
            ErrorBar.Title = "Primera cuenta";
            ErrorBar.Message = File.ReadAllText(UserStore.AvisoClaveInicial).Trim();
            ErrorBar.IsOpen = true;
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _slideshow?.Stop();
        base.OnNavigatedFrom(e);
    }

    private void ApplyBrandLogo()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "logoapp.png");
        if (!File.Exists(path))
        {
            return;
        }

        BrandImage.Source = new BitmapImage(new Uri(path));
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        _slideshow?.Dispose();
        _slideshow = null;
    }

    private void StartDeferredChrome()
    {
        TaskbarJumpList.Clear();
        _slideshow?.Start();
    }

    private void Input_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            TryLogin();
        }
    }

    private void LoginButton_Click(object sender, RoutedEventArgs e) => TryLogin();

    private async void TryLogin()
    {
        ErrorBar.IsOpen = false;
        if (!RedClinica.EstaConectada())
        {
            await RedClinica.AvisarYCerrarAsync(XamlRoot);
            return;
        }

        var username = UsernameBox.Text?.Trim() ?? string.Empty;
        var password = PasswordBox.Password ?? string.Empty;

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            ShowError("Escribe tu usuario y contraseña.");
            return;
        }

        LoginButton.IsEnabled = false;
        try
        {
            var (user, lockout) = await Task.Run<(UserAccount?, string?)>(() =>
            {
                var blocked = App.Security.LockoutMessage(username);
                if (blocked is not null)
                {
                    return (null, blocked);
                }

                return (App.Users.Authenticate(username, password), null);
            });

            if (lockout is not null && user is null)
            {
                ShowError(lockout);
                return;
            }

            if (user is null)
            {
                var failure = await Task.Run(() => App.Security.RegisterFailure(username));
                ShowError(failure ?? "Usuario o contraseña incorrectos, o la cuenta está desactivada.");
                return;
            }

            if (user.MustChangePassword)
            {
                user = await CambioClave.ExigirAsync(user, XamlRoot);
                if (user is null)
                {
                    return;
                }
            }

            var login = await Task.Run(() =>
            {
                var record = App.Logins.Record(user);
                App.Security.RegisterSuccess(user.Username, record.Code);
                return record;
            });
            Session.Current = user;
            Session.LoginCode = login.Code;
            App.Sessions.SignIn(user);
            App.MainAppWindow?.NoteActivity();
            Frame.Navigate(typeof(HomePage));
        }
        finally
        {
            LoginButton.IsEnabled = true;
        }
    }

    private void ShowError(string message)
    {
        ErrorBar.Severity = InfoBarSeverity.Error;
        ErrorBar.Title = "No se pudo iniciar sesión";
        ErrorBar.Message = message;
        ErrorBar.IsOpen = true;
    }
}
