using Iderma.Core;
using Iderma.Core.Catalog;
using Iderma.Core.Models;
using Iderma.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace Iderma_Launcher;

public sealed partial class HomePage : Page
{
    private static string? _codigoComprobado;
    private static int _ultimoSaludo = -1;
    private string? _plantillaSaludo;
    private bool _ocupado;
    private int _photoToken;
    private int _footerClicks;
    private DateTime _footerClickUtc;
    private AppViewMode _view = AppViewMode.Grid;

    public HomePage()
    {
        InitializeComponent();
        _view = AppViewPreference.Load();
        ApplyView(_view);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        VigilanciaAcceso.Comprobar();
        Frame.BackStack.Clear();
        RenovarSaludo();
        App.Users.Changed += OnUsersChanged;
        App.Sessions.Changed += OnSessionsChanged;
        if (!BindCurrentUser())
        {
            Frame.Navigate(typeof(LoginPage));
            return;
        }

        CheckForcedLogout();
        if (Session.Current is not null)
        {
            _ = ComprobarAlIniciarSesionAsync();
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        App.Users.Changed -= OnUsersChanged;
        App.Sessions.Changed -= OnSessionsChanged;
        base.OnNavigatedFrom(e);
    }

    private void OnSessionsChanged()
    {
        DispatcherQueue.TryEnqueue(CheckForcedLogout);
    }

    private void CheckForcedLogout()
    {
        var user = Session.Current;
        if (user is null)
        {
            return;
        }

        if (!App.Sessions.ConsumeForcedLogout(user.Id))
        {
            return;
        }

        App.Security.Record(SecurityEventKind.ForcedSignOut, user.Username, "Cierre por un administrador.");
        AreaAppLauncher.CloseOpenedApps();
        Session.Notice = "Un administrador cerró tu sesión.";
        Session.Clear();
        Frame.Navigate(typeof(LoginPage));
    }

    private void OnUsersChanged()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!BindCurrentUser())
            {
                Frame.Navigate(typeof(LoginPage));
            }
        });
    }

    private bool BindCurrentUser()
    {
        var session = Session.Current;
        if (session is null)
        {
            return false;
        }

        var user = App.Users.FindById(session.Id);
        if (user is null || !user.IsActive)
        {
            App.Sessions.SignOut();
            Session.Clear();
            return false;
        }

        Session.Current = user;
        App.MainAppWindow?.SetHeaderTitle($"{AppInfo.ProductName} · {user.DisplayName}");
        _plantillaSaludo ??= ElegirSaludo();
        GreetingText.Text = string.Format(_plantillaSaludo, user.DisplayName);
        UserPicture.DisplayName = user.DisplayName;
        var token = ++_photoToken;
        _ = ApplyPictureAsync(user.PhotoPath, token);
        var cards = AppCatalog.All
            .Where(app => user.AppIds.Contains(app.Id, StringComparer.OrdinalIgnoreCase))
            .OrderBy(app => app.Status == AppStatus.Private ? 1 : 0)
            .ThenBy(app => app.Id.Equals(AppCatalog.Cuentas, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenBy(app => app.Name, StringComparer.Create(new System.Globalization.CultureInfo("es-ES"), ignoreCase: true))
            .Select(AppCard.From)
            .ToList();

        SubtitleText.Text = cards.Count == 1
            ? $"{user.Role} · 1 aplicación disponible"
            : $"{user.Role} · {cards.Count} aplicaciones disponibles";
        AppsGrid.ItemsSource = cards;
        AppsList.ItemsSource = cards;
        SettingsButton.Visibility = user.CanOpenSettings ? Visibility.Visible : Visibility.Collapsed;
        UpdatesButton.Visibility = UpdateCheckPreference.IsEnabledForCurrentUser()
            ? Visibility.Visible
            : Visibility.Collapsed;
        ToolTipService.SetToolTip(UserPicture, string.IsNullOrWhiteSpace(user.PhotoPath) ? "Sin foto" : "Ver foto");
        FooterText.Text = $"{AppInfo.Version} - Desarrollado por Piero Zavala Ayala - Cualquier duda o consulta contactarme :D";
        TaskbarJumpList.ShowFor(user);
        var pending = TaskbarJumpList.TakePending();
        if (!string.IsNullOrWhiteSpace(pending))
        {
            App.MainAppWindow?.LaunchFromJumpList(pending);
        }

        return true;
    }

    private void RenovarSaludo()
    {
        _plantillaSaludo = ElegirSaludo();
    }

    private static string ElegirSaludo()
    {
        var hora = DateTime.Now.Hour;
        var frases = new List<string>
        {
            "Hola, {0}",
            "Bienvenido, {0}",
            "Ten un buen día, {0}",
            "Qué gusto verte, {0}",
            "Me alegra verte, {0}",
            "Que te vaya bien, {0}",
            "Listo para hoy, {0}",
            hora < 12 ? "Buenos días, {0}" : hora < 19 ? "Buenas tardes, {0}" : "Buenas noches, {0}",
        };

        var indice = Random.Shared.Next(frases.Count);
        if (frases.Count > 1 && indice == _ultimoSaludo)
        {
            indice = (indice + 1) % frases.Count;
        }

        _ultimoSaludo = indice;
        return frases[indice];
    }

    private async Task ApplyPictureAsync(string? path, int token)
    {
        var image = await UserPhoto.LoadAsync(path, 96);
        if (token != _photoToken)
        {
            return;
        }

        UserPicture.ProfilePicture = null;
        UserPicture.ProfilePicture = image;
    }

    private async void UserPicture_Tapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
        var user = Session.Current;
        if (user is null)
        {
            return;
        }

        await UserPhoto.ShowAsync(XamlRoot, user.PhotoPath, user.DisplayName);
    }

    private void ViewMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioMenuFlyoutItem item || item.Tag is not string tag)
        {
            return;
        }

        var mode = tag switch
        {
            "list" => AppViewMode.List,
            "compact" => AppViewMode.Compact,
            _ => AppViewMode.Grid
        };
        if (mode == _view)
        {
            return;
        }

        _view = mode;
        AppViewPreference.Save(mode);
        ApplyView(mode);
    }

    private void ApplyView(AppViewMode mode)
    {
        var showGrid = mode != AppViewMode.List;
        AppsGrid.Visibility = showGrid ? Visibility.Visible : Visibility.Collapsed;
        AppsList.Visibility = showGrid ? Visibility.Collapsed : Visibility.Visible;
        if (mode == AppViewMode.Compact)
        {
            AppsGrid.ItemTemplate = (DataTemplate)Resources["CompactAppTemplate"];
        }
        else if (mode == AppViewMode.Grid)
        {
            AppsGrid.ItemTemplate = (DataTemplate)Resources["GridAppTemplate"];
        }

        ViewButton.Content = mode switch
        {
            AppViewMode.List => "Lista",
            AppViewMode.Compact => "Compacta",
            _ => "Cuadrícula"
        };
        ViewGridItem.IsChecked = mode == AppViewMode.Grid;
        ViewListItem.IsChecked = mode == AppViewMode.List;
        ViewCompactItem.IsChecked = mode == AppViewMode.Compact;
    }

    private async void AppCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        var card = button.DataContext as AppCard;
        var appId = button.Tag as string ?? card?.Id;
        if (string.IsNullOrWhiteSpace(appId))
        {
            return;
        }

        if (card is { CanLaunch: false })
        {
            return;
        }

        var name = card?.Name ?? appId;

        if (AreaAppLauncher.TryGetTarget(appId, out _))
        {
            try
            {
                var error = AreaAppLauncher.Launch(appId);
                if (string.IsNullOrEmpty(error))
                {
                    return;
                }

                if (error == AreaAppLauncher.AlreadyRunning)
                {
                    await RunningAppDialog.ShowAsync(XamlRoot, appId, name);
                    return;
                }

                await ShowAppDialog(name, error);
            }
            catch (Exception ex)
            {
                await ShowAppDialog(name, $"No se pudo abrir {name}.\n{ex.Message}");
            }

            return;
        }

        await ShowAppDialog(name, $"{name} aún no tiene un programa asociado en este equipo.");
    }

    private async Task ShowAppDialog(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            PrimaryButtonText = "Cerrar",
            XamlRoot = XamlRoot
        };
        await dialog.ShowAsync();
    }

    private async Task ComprobarAlIniciarSesionAsync()
    {
        if (!UpdateCheckPreference.IsEnabledForCurrentUser())
        {
            return;
        }

        var codigo = Session.LoginCode;
        if (string.IsNullOrWhiteSpace(codigo) || codigo == _codigoComprobado)
        {
            return;
        }

        if (await BuscarActualizacionAsync(preguntar: true))
        {
            _codigoComprobado = codigo;
        }
    }

    private async void UpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        if (!UpdateCheckPreference.IsEnabledForCurrentUser())
        {
            return;
        }

        await BuscarActualizacionAsync(preguntar: true);
    }

    private async Task<bool> BuscarActualizacionAsync(bool preguntar)
    {
        if (_ocupado)
        {
            return false;
        }

        _ocupado = true;
        UpdatesButton.IsEnabled = false;
        UpdateBar.ActionButton = null;
        UpdateBar.IsOpen = false;
        UpdateProgress.Visibility = Visibility.Collapsed;
        MostrarEstadoTexto("Buscando actualizaciones…", alDia: false);

        try
        {
            using var limite = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            var release = await ActualizacionProgramaService.ConsultarAsync(limite.Token);
            if (!ActualizacionProgramaService.HayActualizacion(release))
            {
                UpdateBar.IsOpen = false;
                MostrarEstadoTexto("Su aplicación está actualizada", alDia: true);
                return true;
            }

            UpdateStatusText.Visibility = Visibility.Collapsed;

            var pendiente = ActualizacionProgramaService.LeerInstaladorPendiente();
            if (pendiente is not null
                && string.Equals(pendiente.Etiqueta, release.Etiqueta, StringComparison.OrdinalIgnoreCase))
            {
                MostrarListaParaInstalar(pendiente.Etiqueta, pendiente.Ruta);
                if (preguntar)
                {
                    await PreguntarInstalacionAsync(pendiente.Etiqueta, pendiente.Ruta);
                }

                return true;
            }

            var accion = new Button { Content = "Descargar e instalar" };
            accion.Click += async (_, _) => await DescargarEInstalarAsync(release);
            UpdateBar.ActionButton = accion;
            UpdateBar.Severity = InfoBarSeverity.Warning;
            UpdateBar.Title = "Hay una versión nueva";
            UpdateBar.Message =
                $"{release.Etiqueta}{ActualizacionProgramaService.Tamano(release)} está en GitHub y actualiza el launcher y las áreas juntas.";
            UpdateBar.IsOpen = true;

            if (preguntar)
            {
                await DescargarEInstalarAsync(release);
            }
        }
        catch (Exception ex)
        {
            var pendiente = ActualizacionProgramaService.LeerInstaladorPendiente();
            if (pendiente is not null)
            {
                MostrarListaParaInstalar(pendiente.Etiqueta, pendiente.Ruta);
                if (preguntar)
                {
                    await PreguntarInstalacionAsync(pendiente.Etiqueta, pendiente.Ruta);
                }

                return true;
            }

            UpdateStatusText.Visibility = Visibility.Collapsed;
            UpdateBar.Severity = InfoBarSeverity.Error;
            UpdateBar.Title = "No se pudo comprobar";
            UpdateBar.Message = ex.Message;
            UpdateBar.IsOpen = true;
            return false;
        }
        finally
        {
            _ocupado = false;
            UpdatesButton.IsEnabled = true;
        }

        return true;
    }

    private void MostrarEstadoTexto(string texto, bool alDia)
    {
        var clave = alDia ? "SystemFillColorSuccessBrush" : "TextFillColorSecondaryBrush";
        if (Application.Current.Resources.TryGetValue(clave, out var recurso) && recurso is Brush pincel)
        {
            UpdateStatusText.Foreground = pincel;
        }

        UpdateStatusText.Text = texto;
        UpdateStatusText.Visibility = Visibility.Visible;
    }

    private async Task DescargarEInstalarAsync(ReleasePrograma release)
    {
        if (!IsLoaded)
        {
            return;
        }

        var dialogo = new ContentDialog
        {
            Title = "Actualizar todo Iderma",
            Content =
                $"Se descargará {release.Etiqueta}{ActualizacionProgramaService.Tamano(release)}.\n\n" +
                "El instalador actualiza el programa completo. Se cerrarán las áreas abiertas y el launcher para poder reemplazar los archivos.",
            PrimaryButtonText = "Descargar e instalar",
            CloseButtonText = "Más tarde",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };
        if (await dialogo.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        UpdatesButton.IsEnabled = false;
        UpdateBar.ActionButton = null;
        UpdateProgress.Visibility = Visibility.Visible;
        UpdateProgress.Value = 0;
        try
        {
            IProgress<(double Porcentaje, string Mensaje)> progreso = new Progress<(double Porcentaje, string Mensaje)>(p =>
            {
                UpdateProgress.Value = p.Porcentaje;
                UpdateBar.Message = p.Mensaje;
            });
            var ruta = await ActualizacionProgramaService.DescargarInstaladorAsync(release, progreso);
            ActualizacionProgramaService.GuardarInstaladorPendiente(ruta, release);
            UpdateProgress.Value = 100;
            Instalar(ruta);
        }
        catch (Exception ex)
        {
            UpdateBar.Severity = InfoBarSeverity.Error;
            UpdateBar.Title = "No se pudo descargar";
            UpdateBar.Message = ex.Message;
            UpdateProgress.Visibility = Visibility.Collapsed;
            UpdatesButton.IsEnabled = true;
        }
    }

    private void MostrarListaParaInstalar(string etiqueta, string ruta)
    {
        UpdateStatusText.Visibility = Visibility.Collapsed;
        UpdateBar.Severity = InfoBarSeverity.Warning;
        UpdateBar.Title = "Instalador listo";
        UpdateBar.Message = $"{etiqueta} está descargado. Instálelo para dejar todo Iderma en la misma versión.";
        UpdateBar.IsOpen = true;
        var accion = new Button { Content = "Instalar y cerrar" };
        accion.Click += (_, _) => Instalar(ruta);
        UpdateBar.ActionButton = accion;
    }

    private async Task PreguntarInstalacionAsync(string etiqueta, string ruta)
    {
        if (!IsLoaded)
        {
            return;
        }

        var dialogo = new ContentDialog
        {
            Title = "Instalar actualización",
            Content =
                $"Se instalará {etiqueta}.\n\n" +
                "Se cerrarán las áreas que estén abiertas y este launcher.",
            PrimaryButtonText = "Instalar y cerrar",
            CloseButtonText = "Más tarde",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };
        if (await dialogo.ShowAsync() == ContentDialogResult.Primary)
        {
            Instalar(ruta);
        }
    }

    private static void Instalar(string ruta)
    {
        ActualizacionProgramaService.LanzarInstalador(ruta);
        App.MainAppWindow?.CerrarParaActualizar();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (Session.Current is not { CanOpenSettings: true })
        {
            return;
        }

        Frame.Navigate(typeof(SettingsPage));
    }

    private void LogoutButton_Click(object sender, RoutedEventArgs e)
    {
        var username = Session.Current?.Username;
        App.Sessions.SignOut();
        Session.Clear();
        if (!string.IsNullOrWhiteSpace(username))
        {
            App.Security.Record(SecurityEventKind.SignOut, username, "Cierre de sesión.");
        }

        Frame.Navigate(typeof(LoginPage));
    }

    private void Footer_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        var now = DateTime.UtcNow;
        if (now - _footerClickUtc > TimeSpan.FromSeconds(3))
        {
            _footerClicks = 0;
        }

        _footerClickUtc = now;
        _footerClicks++;
        if (_footerClicks < 5)
        {
            return;
        }

        _footerClicks = 0;
        ArcadeWindow.Open();
    }
}
