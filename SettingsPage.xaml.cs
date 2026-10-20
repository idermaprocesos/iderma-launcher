using Iderma.Core.Models;
using Iderma.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Windows.Storage.Pickers;

namespace Iderma_Launcher;

public sealed partial class SettingsPage : Page
{
    private bool _ready;
    private int _photoToken;

    public SettingsPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        App.Users.Changed += OnSecuritySignal;
        App.Sessions.Changed += OnSecuritySignal;
        if (!EnsureSession())
        {
            return;
        }

        BindAccount();
        BindDevOptions();
        BindPolicy();
        BindActivity();
        BindLogins();
        _ready = true;
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _ready = false;
        App.Users.Changed -= OnSecuritySignal;
        App.Sessions.Changed -= OnSecuritySignal;
        base.OnNavigatedFrom(e);
    }

    private void OnSecuritySignal() => DispatcherQueue.TryEnqueue(() =>
    {
        if (!EnsureSession())
        {
            return;
        }

        BindAccount();
        BindLogins();
    });

    private bool EnsureSession()
    {
        var user = Session.Current;
        if (user is null)
        {
            Frame.Navigate(typeof(LoginPage));
            return false;
        }

        if (App.Sessions.ConsumeForcedLogout(user.Id))
        {
            App.Security.Record(SecurityEventKind.ForcedSignOut, user.Username, "Cierre por un administrador.");
            AreaAppLauncher.CloseOpenedApps();
            Session.Notice = "Un administrador cerró tu sesión.";
            Session.Clear();
            Frame.Navigate(typeof(LoginPage));
            return false;
        }

        var account = App.Users.FindById(user.Id);
        if (account is null || !account.IsActive)
        {
            App.Security.Record(SecurityEventKind.SignOut, user.Username, "Cuenta desactivada.");
            App.Sessions.SignOut();
            Session.Clear();
            Frame.Navigate(typeof(LoginPage));
            return false;
        }

        Session.Current = account;
        if (!account.CanOpenSettings)
        {
            Frame.Navigate(typeof(HomePage));
            return false;
        }

        return true;
    }

    private void BindAccount()
    {
        var user = Session.Current;
        if (user is null)
        {
            return;
        }

        AccountNameText.Text = user.DisplayName;
        AccountMetaText.Text = $"{user.Username} · {user.Role}";
        var started = App.Sessions.Snapshot().StartedAt;
        SessionStartText.Text = started is null
            ? "Sesión actual sin hora de inicio."
            : $"Sesión actual desde {started:dd/MM/yyyy HH:mm}.";
        var previous = App.Security.PreviousSignIn(user.Username);
        PreviousAccessText.Text = previous is null
            ? "Sin accesos anteriores registrados."
            : $"Acceso anterior: {previous:dd/MM/yyyy HH:mm}.";
        SessionCodeText.Text = string.IsNullOrWhiteSpace(Session.LoginCode)
            ? "Esta sesión no tiene código interno."
            : $"Código interno de esta sesión: {Session.LoginCode}";
        AccountPicture.DisplayName = user.DisplayName;
        var token = ++_photoToken;
        _ = ApplyPhotoAsync(user.PhotoPath, token);
    }

    private async Task ApplyPhotoAsync(string? path, int token)
    {
        var image = await UserPhoto.LoadAsync(path, 256);
        if (token != _photoToken)
        {
            return;
        }

        AccountPicture.ProfilePicture = null;
        AccountPicture.ProfilePicture = image;
        var hasPhoto = image is not null;
        ExpandPhotoButton.Visibility = hasPhoto ? Visibility.Visible : Visibility.Collapsed;
        RemovePhotoButton.Visibility = hasPhoto ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void Photo_Tapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
        await ShowPhotoAsync();
    }

    private async void ExpandPhoto_Click(object sender, RoutedEventArgs e) => await ShowPhotoAsync();

    private async void WorkflowImage_Tapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "workflow.png");
        await UserPhoto.ShowAsync(
            XamlRoot,
            path,
            "Flujo de trabajo",
            "No se encontró la imagen del flujo de trabajo.");
    }

    private async Task ShowPhotoAsync()
    {
        var user = Session.Current;
        if (user is null)
        {
            return;
        }

        await UserPhoto.ShowAsync(XamlRoot, user.PhotoPath, user.DisplayName);
    }

    private async void ChangePhoto_Click(object sender, RoutedEventArgs e)
    {
        var user = Session.Current;
        if (user is null || App.MainAppWindow is null)
        {
            return;
        }

        try
        {
            var picker = new FileOpenPicker(App.MainAppWindow.AppWindow.Id)
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary,
                ViewMode = PickerViewMode.Thumbnail,
                CommitButtonText = "Usar esta foto"
            };
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".webp");

            var result = await picker.PickSingleFileAsync();
            if (result is null || string.IsNullOrWhiteSpace(result.Path))
            {
                return;
            }

            var updated = App.Users.SetPhoto(user.Id, result.Path);
            Session.Current = updated;
            AccountPicture.DisplayName = updated.DisplayName;
            var token = ++_photoToken;
            await ApplyPhotoAsync(updated.PhotoPath, token);
            ShowResult(InfoBarSeverity.Success, "Foto", "La foto de la cuenta se actualizó.");
        }
        catch (Exception ex)
        {
            ShowResult(InfoBarSeverity.Error, "Foto", ex.Message);
        }
    }

    private async void RemovePhoto_Click(object sender, RoutedEventArgs e)
    {
        var user = Session.Current;
        if (user is null || string.IsNullOrWhiteSpace(user.PhotoPath))
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "Quitar foto",
            Content = "La cuenta volverá a mostrar las iniciales.",
            PrimaryButtonText = "Quitar",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            var updated = App.Users.ClearPhoto(user.Id);
            Session.Current = updated;
            AccountPicture.DisplayName = updated.DisplayName;
            var token = ++_photoToken;
            await ApplyPhotoAsync(updated.PhotoPath, token);
            ShowResult(InfoBarSeverity.Success, "Foto", "Se quitó la foto de la cuenta.");
        }
        catch (Exception ex)
        {
            ShowResult(InfoBarSeverity.Error, "Foto", ex.Message);
        }
    }

    private void BindDevOptions()
    {
        var user = Session.Current;
        var isDev = user?.Role.Equals("Desarrollador", StringComparison.OrdinalIgnoreCase) == true;
        DevUpdatesCard.Visibility = isDev ? Visibility.Visible : Visibility.Collapsed;
        if (!isDev)
        {
            return;
        }

        _ready = false;
        UpdateCheckSwitch.IsOn = UpdateCheckPreference.Load();
        _ready = true;
    }

    private void UpdateCheckSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_ready)
        {
            return;
        }

        UpdateCheckPreference.Save(UpdateCheckSwitch.IsOn);
    }

    private void BindPolicy()
    {
        _ready = false;
        var policy = App.Security.GetPolicy();
        IdleSwitch.IsOn = policy.IdleLockMinutes > 0;
        IdleMinutesBox.Value = policy.IdleLockMinutes > 0 ? policy.IdleLockMinutes : 15;
        IdleMinutesBox.IsEnabled = IdleSwitch.IsOn;
        LockoutSwitch.IsOn = policy.LockoutEnabled;
        AttemptsBox.Value = policy.MaxFailedAttempts <= 0 ? 5 : policy.MaxFailedAttempts;
        LockoutMinutesBox.Value = policy.LockoutMinutes <= 0 ? 5 : policy.LockoutMinutes;
        AttemptsBox.IsEnabled = LockoutSwitch.IsOn;
        LockoutMinutesBox.IsEnabled = LockoutSwitch.IsOn;
        RefreshStatus(policy);
        _ready = true;
    }

    private void BindActivity()
    {
        var user = Session.Current;
        if (user is null)
        {
            return;
        }

        var isAdmin = user.Role.Equals("Administrador", StringComparison.OrdinalIgnoreCase);
        ActivityScopeText.Text = isAdmin
            ? "Registro local del equipo. Visible para administradores."
            : "Registro local de esta cuenta. No incluye contraseñas.";

        var rows = App.Security.Recent(user.Username, isAdmin)
            .Select(item => new SecurityEventRow
            {
                Title = TitleFor(item.Kind) + (isAdmin ? $" · {item.Username}" : string.Empty),
                When = item.At.ToString("dd/MM/yyyy HH:mm"),
                Detail = item.Detail
            })
            .ToList();

        ActivityList.ItemsSource = rows;
        var empty = rows.Count == 0;
        ActivityEmptyText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        ActivityList.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
    }

    private void BindLogins()
    {
        var user = Session.Current;
        if (user is null)
        {
            return;
        }

        var isAdmin = user.Role.Equals("Administrador", StringComparison.OrdinalIgnoreCase);
        ExportLoginsButton.Visibility = user.CanExportLogins ? Visibility.Visible : Visibility.Collapsed;
        LoginScopeText.Text = !user.CanExportLogins
            ? "Este perfil puede ver el registro de inicios, pero no tiene permiso para exportarlo."
            : isAdmin
                ? "Cada inicio de sesión guarda un código interno y los datos del acceso. Como administrador puedes exportar todos los del equipo."
                : "Cada inicio de sesión guarda un código interno y los datos del acceso. Puedes exportar los de esta cuenta.";

        var rows = App.Logins.List(user.Username, isAdmin)
            .Take(40)
            .Select(item => new LoginRecordRow
            {
                Title = item.Code,
                When = item.At.ToString("dd/MM/yyyy HH:mm:ss"),
                Detail = isAdmin
                    ? $"{item.DisplayName} ({item.Username}) · {item.Role} · {item.MachineName} · {item.WindowsAccount}"
                    : $"{item.Role} · {item.MachineName} · {item.WindowsAccount} · {item.OperatingSystem}"
            })
            .ToList();

        LoginList.ItemsSource = rows;
        var empty = rows.Count == 0;
        LoginEmptyText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        LoginList.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void ExportLogins_Click(object sender, RoutedEventArgs e)
    {
        var user = Session.Current;
        if (user is null || !user.CanExportLogins || App.MainAppWindow is null)
        {
            return;
        }

        try
        {
            var isAdmin = user.Role.Equals("Administrador", StringComparison.OrdinalIgnoreCase);
            var records = App.Logins.List(user.Username, isAdmin);
            if (records.Count == 0)
            {
                ShowResult(InfoBarSeverity.Informational, "Exportar", "Todavía no hay inicios de sesión para exportar.");
                return;
            }

            var picker = new FileSavePicker(App.MainAppWindow.AppWindow.Id)
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                SuggestedFileName = $"iderma-inicios-{DateTime.Now:yyyyMMdd-HHmm}",
                DefaultFileExtension = ".csv",
                CommitButtonText = "Exportar"
            };
            picker.FileTypeChoices.Add("CSV", new List<string> { ".csv" });

            var result = await picker.PickSaveFileAsync();
            if (result is null)
            {
                return;
            }

            await File.WriteAllTextAsync(result.Path, App.Logins.ToCsv(records));
            ShowResult(InfoBarSeverity.Success, "Exportar", $"Se exportaron {records.Count} inicios de sesión.");
        }
        catch (Exception ex)
        {
            ShowResult(InfoBarSeverity.Error, "Exportar", ex.Message);
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(HomePage));

    private void IniciarGuia_Click(object sender, RoutedEventArgs e) =>
        App.MainAppWindow?.IniciarGuiaInicioRapido();

    private async void IdleSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_ready)
        {
            return;
        }

        if (!IdleSwitch.IsOn && !await ConfirmPasswordAsync("Para desactivar el bloqueo por inactividad confirma tu contraseña."))
        {
            _ready = false;
            IdleSwitch.IsOn = true;
            _ready = true;
            return;
        }

        IdleMinutesBox.IsEnabled = IdleSwitch.IsOn;
        PersistPolicy(announce: true);
    }

    private async void LockoutSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_ready)
        {
            return;
        }

        if (!LockoutSwitch.IsOn && !await ConfirmPasswordAsync("Para desactivar el bloqueo por intentos fallidos confirma tu contraseña."))
        {
            _ready = false;
            LockoutSwitch.IsOn = true;
            _ready = true;
            return;
        }

        AttemptsBox.IsEnabled = LockoutSwitch.IsOn;
        LockoutMinutesBox.IsEnabled = LockoutSwitch.IsOn;
        PersistPolicy(announce: true);
    }

    private void ProtectionValue_Changed(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_ready || double.IsNaN(args.NewValue))
        {
            return;
        }

        PersistPolicy(announce: false);
    }

    private void PersistPolicy(bool announce)
    {
        var user = Session.Current;
        if (user is null)
        {
            return;
        }

        var policy = new SecurityPolicy
        {
            IdleLockMinutes = IdleSwitch.IsOn ? (int)Math.Round(IdleMinutesBox.Value) : 0,
            LockoutEnabled = LockoutSwitch.IsOn,
            MaxFailedAttempts = (int)Math.Round(AttemptsBox.Value),
            LockoutMinutes = (int)Math.Round(LockoutMinutesBox.Value)
        };

        App.Security.SavePolicy(policy, user.Username);
        var saved = App.Security.GetPolicy();
        RefreshStatus(saved);
        if (announce)
        {
            ShowResult(InfoBarSeverity.Success, "Protección", "La configuración de seguridad se guardó.");
        }

        BindActivity();
        BindLogins();
    }

    private void RefreshStatus(SecurityPolicy policy)
    {
        var idle = policy.IdleLockMinutes > 0
            ? $"Bloqueo por inactividad a los {policy.IdleLockMinutes} min."
            : "Bloqueo por inactividad desactivado.";
        var lockout = policy.LockoutEnabled
            ? $"Bloqueo tras {policy.MaxFailedAttempts} intentos fallidos, durante {policy.LockoutMinutes} min."
            : "Bloqueo por intentos fallidos desactivado.";
        ProtectionText.Text = $"{idle} {lockout}";
    }

    private async Task<bool> ConfirmPasswordAsync(string message)
    {
        var user = Session.Current;
        if (user is null)
        {
            return false;
        }

        var box = new PasswordBox { PlaceholderText = "Contraseña actual" };
        var dialog = new ContentDialog
        {
            Title = "Confirmar identidad",
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                    box
                }
            },
            PrimaryButtonText = "Confirmar",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return false;
        }

        return App.Users.Authenticate(user.Username, box.Password) is not null;
    }

    private async void ComprobarBase_Click(object sender, RoutedEventArgs e)
    {
        BotonComprobarBase.IsEnabled = false;
        AnilloBase.IsActive = true;
        AnilloBase.Visibility = Visibility.Visible;
        BarraBase.IsOpen = false;
        TextoErrorBase.Visibility = Visibility.Collapsed;
        TextoSolucionBase.Visibility = Visibility.Collapsed;

        try
        {
            var resultado = await DiagnosticoAccesoBase.EjecutarAsync();
            BarraBase.Severity = resultado.Correcto ? InfoBarSeverity.Success : InfoBarSeverity.Error;
            BarraBase.Title = "Base de datos";
            BarraBase.Message = resultado.Resumen;
            BarraBase.IsOpen = true;

            if (!string.IsNullOrWhiteSpace(resultado.Error))
            {
                TextoErrorBase.Text = "Error: " + resultado.Error;
                TextoErrorBase.Visibility = Visibility.Visible;
            }

            if (!string.IsNullOrWhiteSpace(resultado.Solucion))
            {
                TextoSolucionBase.Text = resultado.Correcto
                    ? resultado.Solucion
                    : "Cómo solucionarlo: " + resultado.Solucion;
                TextoSolucionBase.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            BarraBase.Severity = InfoBarSeverity.Error;
            BarraBase.Title = "Base de datos";
            BarraBase.Message = "La comprobación no pudo terminar.";
            BarraBase.IsOpen = true;
            TextoErrorBase.Text = "Error: " + ex.Message;
            TextoErrorBase.Visibility = Visibility.Visible;
            TextoSolucionBase.Text = "Cierra el launcher, ábrelo de nuevo y repite la comprobación. Si el mensaje se repite, compártelo con quien administra Iderma.";
            TextoSolucionBase.Visibility = Visibility.Visible;
        }
        finally
        {
            AnilloBase.IsActive = false;
            AnilloBase.Visibility = Visibility.Collapsed;
            BotonComprobarBase.IsEnabled = true;
        }
    }

    private void ShowResult(InfoBarSeverity severity, string title, string message)
    {
        ResultBar.Severity = severity;
        ResultBar.Title = title;
        ResultBar.Message = message;
        ResultBar.IsOpen = true;
    }

    private static string TitleFor(string kind) => kind switch
    {
        SecurityEventKind.SignIn => "Inicio de sesión",
        SecurityEventKind.SignOut => "Cierre de sesión",
        SecurityEventKind.SignInFailed => "Intento fallido",
        SecurityEventKind.Lockout => "Acceso bloqueado",
        SecurityEventKind.PasswordChanged => "Contraseña actualizada",
        SecurityEventKind.PolicyChanged => "Protección actualizada",
        SecurityEventKind.SessionLocked => "Sesión bloqueada",
        SecurityEventKind.SessionUnlocked => "Sesión reanudada",
        SecurityEventKind.ForcedSignOut => "Cierre por administración",
        _ => "Evento de seguridad"
    };
}

public sealed class SecurityEventRow
{
    public string Title { get; set; } = string.Empty;
    public string When { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
}

public sealed class LoginRecordRow
{
    public string Title { get; set; } = string.Empty;
    public string When { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
}
