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
    private int _estadoAppsToken;
    private HashSet<string> _appsConActualizacion = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _dialogoLock = new(1, 1);

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
        App.Meetings.Changed += OnMeetingsChanged;
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
        App.Meetings.Changed -= OnMeetingsChanged;
        base.OnNavigatedFrom(e);
    }

    private void OnMeetingsChanged()
    {
        DispatcherQueue.TryEnqueue(PintarReuniones);
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
        PintarTarjetas(user);
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

        _ = RefrescarEstadosAppsAsync(user);
        return true;
    }

    private void PintarTarjetas(UserAccount user)
    {
        var cards = AppCatalog.All
            .Where(app => user.AppIds.Contains(app.Id, StringComparer.OrdinalIgnoreCase))
            .OrderBy(app => app.Status == AppStatus.Private ? 1 : 0)
            .ThenBy(app => app.Id.Equals(AppCatalog.Cuentas, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenBy(app => app.Name, StringComparer.Create(new System.Globalization.CultureInfo("es-ES"), ignoreCase: true))
            .Select(app => AppCard.From(app, EstadoDeApp(app.Id)))
            .ToList();

        SubtitleText.Text = cards.Count == 1
            ? $"{user.Role} · 1 aplicación disponible"
            : $"{user.Role} · {cards.Count} aplicaciones disponibles";
        AppsGrid.ItemsSource = cards;
        AppsList.ItemsSource = cards;
        PintarReuniones();
    }

    private void PintarReuniones()
    {
        var user = Session.Current;
        if (user is null)
        {
            MeetingsGrid.ItemsSource = null;
            NoMeetingsText.Visibility = Visibility.Collapsed;
            MeetingsSubtitle.Text = "Próximas reuniones del equipo";
            return;
        }

        var cards = App.Meetings.GetUpcoming()
            .Select(m => MeetingCard.From(m, user.Id))
            .ToList();
        MeetingsGrid.ItemsSource = cards;
        NoMeetingsText.Visibility = cards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        MeetingsSubtitle.Text = cards.Count switch
        {
            0 => "Sin reuniones próximas",
            1 => "1 reunión próxima",
            _ => $"{cards.Count} reuniones próximas"
        };
    }

    private async void ScheduleMeeting_Click(object sender, RoutedEventArgs e)
    {
        var user = Session.Current;
        if (user is null || XamlRoot is null)
        {
            return;
        }

        var dialog = new ScheduleMeetingDialog(user, App.Users.GetAll())
        {
            XamlRoot = XamlRoot
        };
        if (await MostrarDialogoAsync(dialog) != ContentDialogResult.Primary || dialog.Resultado is null)
        {
            return;
        }

        App.Meetings.Add(dialog.Resultado);
        PintarReuniones();
    }

    private async void MeetingHistory_Click(object sender, RoutedEventArgs e)
    {
        var user = Session.Current;
        if (user is null || XamlRoot is null)
        {
            return;
        }

        var cards = App.Meetings.GetPast()
            .Select(m => MeetingCard.From(m, user.Id))
            .ToList();
        var dialog = new MeetingHistoryDialog(cards)
        {
            XamlRoot = XamlRoot
        };
        await MostrarDialogoAsync(dialog);
    }

    private async void MeetingCard_Tapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
        var user = Session.Current;
        if (user is null || sender is not FrameworkElement element)
        {
            return;
        }

        var meetingId = element.Tag as string
            ?? (element.DataContext as MeetingCard)?.Id;
        if (string.IsNullOrWhiteSpace(meetingId))
        {
            return;
        }

        var meeting = App.Meetings.FindById(meetingId);
        if (meeting is null)
        {
            return;
        }

        var detalle = new MeetingDetailsDialog(meeting, user.Id)
        {
            XamlRoot = XamlRoot
        };
        var resultadoDetalle = await MostrarDialogoAsync(detalle);
        if (detalle.Guardado)
        {
            PintarReuniones();
        }

        if (resultadoDetalle != ContentDialogResult.Primary)
        {
            return;
        }

        // Primary = Editar
        meeting = App.Meetings.FindById(meetingId) ?? meeting;
        var editor = new ScheduleMeetingDialog(user, App.Users.GetAll(), meeting)
        {
            XamlRoot = XamlRoot
        };
        if (await MostrarDialogoAsync(editor) != ContentDialogResult.Primary || editor.Resultado is null)
        {
            return;
        }

        App.Meetings.Update(editor.Resultado);
        PintarReuniones();
    }

    private void MeetingCard_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        e.Handled = true;
        var user = Session.Current;
        if (user is null || sender is not FrameworkElement element)
        {
            return;
        }

        var meetingId = element.Tag as string
            ?? (element.DataContext as MeetingCard)?.Id;
        if (string.IsNullOrWhiteSpace(meetingId))
        {
            return;
        }

        var card = element.DataContext as MeetingCard
            ?? (MeetingsGrid.ItemsSource as IEnumerable<MeetingCard>)?.FirstOrDefault(c => c.Id == meetingId);
        if (card is null || card.IsPast)
        {
            return;
        }

        var flyout = new MenuFlyout();
        var si = new MenuFlyoutItem
        {
            Text = "Sí participo",
            Tag = (meetingId, MeetingResponse.Accepted)
        };
        si.Icon = new BitmapIcon { UriSource = new Uri(card.YesIconPath), ShowAsMonochrome = false };
        si.Click += MeetingResponseMenu_Click;

        var no = new MenuFlyoutItem
        {
            Text = "No participo",
            Tag = (meetingId, MeetingResponse.Declined)
        };
        no.Icon = new BitmapIcon { UriSource = new Uri(card.NoIconPath), ShowAsMonochrome = false };
        no.Click += MeetingResponseMenu_Click;

        flyout.Items.Add(si);
        flyout.Items.Add(no);
        flyout.ShowAt(element, e.GetPosition(element));
    }

    private void MeetingResponseMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { Tag: (string meetingId, MeetingResponse response) })
        {
            return;
        }

        ResponderReunion(meetingId, response);
    }

    private void ResponderReunion(string meetingId, MeetingResponse response)
    {
        var user = Session.Current;
        if (user is null || string.IsNullOrWhiteSpace(meetingId))
        {
            return;
        }

        if (App.Meetings.SetResponse(meetingId, user.Id, user.DisplayName, response))
        {
            PintarReuniones();
        }
    }

    private AppCardState EstadoDeApp(string appId)
    {
        if (!AreaAppLauncher.IsInstalled(appId))
        {
            return AppCardState.Missing;
        }

        return _appsConActualizacion.Contains(appId)
            ? AppCardState.UpdateRequired
            : AppCardState.Ready;
    }

    private async Task RefrescarEstadosAppsAsync(UserAccount user)
    {
        var token = ++_estadoAppsToken;
        try
        {
            using var limite = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            var pendientes = await ActualizacionProgramaService.ConsultarParaUsuarioAsync(
                user.AppIds,
                AreaAppLauncher.VersionInstalada,
                limite.Token);
            if (token != _estadoAppsToken || Session.Current?.Id != user.Id)
            {
                return;
            }

            _appsConActualizacion = pendientes
                .Where(p => !p.Id.Equals(ActualizacionProgramaService.IdLauncher, StringComparison.OrdinalIgnoreCase))
                .Select(p => p.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            PintarTarjetas(user);
        }
        catch
        {
        }
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
        AplicarLayoutReuniones(mode);
    }

    private void OuterScroll_SizeChanged(object sender, SizeChangedEventArgs e) =>
        AplicarLayoutReuniones(_view);

    private void AplicarLayoutReuniones(AppViewMode mode)
    {
        if (ContentLayout is null || MeetingsHost is null)
        {
            return;
        }

        var lista = mode == AppViewMode.List;
        if (lista)
        {
            OuterScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            OuterScroll.VerticalScrollMode = ScrollMode.Disabled;
            AppsScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            AppsScroll.VerticalScrollMode = ScrollMode.Enabled;
            MeetingsScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            MeetingsScroll.VerticalScrollMode = ScrollMode.Enabled;

            var alto = OuterScroll.ActualHeight;
            if (alto > 0)
            {
                ContentLayout.MinHeight = alto;
                ContentLayout.Height = alto;
            }

            StackedMeetingsRow.Height = new GridLength(0);
            SideDividerCol.Width = new GridLength(1);
            SideMeetingsCol.Width = new GridLength(340);
            SideDivider.Visibility = Visibility.Visible;
            StackedDivider.Visibility = Visibility.Collapsed;
            MeetingsHost.Padding = new Thickness(4, 0, 0, 0);
            MeetingsHost.ClearValue(FrameworkElement.MarginProperty);
            Grid.SetRow(MeetingsHost, 0);
            Grid.SetColumn(MeetingsHost, 2);
            Grid.SetRow(SideDivider, 0);
            Grid.SetColumn(SideDivider, 1);
        }
        else
        {
            OuterScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            OuterScroll.VerticalScrollMode = ScrollMode.Enabled;
            AppsScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            AppsScroll.VerticalScrollMode = ScrollMode.Disabled;
            MeetingsScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            MeetingsScroll.VerticalScrollMode = ScrollMode.Disabled;

            ContentLayout.ClearValue(FrameworkElement.MinHeightProperty);
            ContentLayout.ClearValue(FrameworkElement.HeightProperty);

            StackedMeetingsRow.Height = GridLength.Auto;
            SideDividerCol.Width = new GridLength(0);
            SideMeetingsCol.Width = new GridLength(0);
            SideDivider.Visibility = Visibility.Collapsed;
            StackedDivider.Visibility = Visibility.Visible;
            MeetingsHost.Padding = new Thickness(0, 4, 0, 0);
            Grid.SetRow(MeetingsHost, 1);
            Grid.SetColumn(MeetingsHost, 0);
        }
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
        var estado = card?.State ?? EstadoDeApp(appId);
        if (estado is AppCardState.Missing or AppCardState.UpdateRequired)
        {
            await InstalarOActualizarAppAsync(appId, name, estado);
            return;
        }

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

    private async Task InstalarOActualizarAppAsync(string appId, string name, AppCardState estado)
    {
        var app = AppCatalog.Find(appId);
        if (app is null || string.IsNullOrWhiteSpace(app.GitHubRepo))
        {
            await ShowAppDialog(name, "Esta aplicación no tiene un repositorio de descarga configurado.");
            return;
        }

        if (!AreaAppLauncher.TryGetTarget(appId, out _))
        {
            await ShowAppDialog(name, $"{name} aún no tiene un programa asociado en este equipo.");
            return;
        }

        var accion = estado == AppCardState.Missing ? "instalar" : "actualizar";
        var dialogo = new ContentDialog
        {
            Title = estado == AppCardState.Missing ? $"Instalar {name}" : $"Actualizar {name}",
            Content =
                estado == AppCardState.Missing
                    ? $"{name} no está instalada en este equipo. Se descargará desde GitHub y no podrá usarla hasta terminar."
                    : $"Hay una versión nueva de {name}. Debe actualizarla antes de entrar.",
            PrimaryButtonText = estado == AppCardState.Missing ? "Descargar e instalar" : "Descargar y actualizar",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Primary
        };
        if (await MostrarDialogoAsync(dialogo) != ContentDialogResult.Primary)
        {
            return;
        }

        // Progreso en InfoBar (evita un segundo ContentDialog en paralelo).
        UpdateBar.ActionButton = null;
        UpdateBar.Severity = InfoBarSeverity.Informational;
        UpdateBar.Title = name;
        UpdateBar.Message = $"Preparando {accion}…";
        UpdateBar.IsOpen = true;
        UpdateProgress.Visibility = Visibility.Visible;
        UpdateProgress.Value = 0;
        UpdateStatusText.Visibility = Visibility.Collapsed;

        try
        {
            var local = AreaAppLauncher.VersionInstalada(appId) ?? new Version(0, 0, 0, 0);
            var componente = await ActualizacionProgramaService.ConsultarComponenteAsync(
                appId,
                name,
                app.GitHubRepo,
                local);
            if (componente is null && estado == AppCardState.Missing)
            {
                componente = await ActualizacionProgramaService.ConsultarComponenteAsync(
                    appId,
                    name,
                    app.GitHubRepo,
                    new Version(0, 0, 0, 0));
            }

            if (componente is null)
            {
                UpdateBar.IsOpen = false;
                UpdateProgress.Visibility = Visibility.Collapsed;
                await ShowAppDialog(
                    name,
                    $"No hay un paquete publicado todavía en {app.GitHubRepo}. Sube un release con .zip o .exe en ese repositorio.");
                return;
            }

            IProgress<(double Porcentaje, string Mensaje)> progreso =
                new Progress<(double Porcentaje, string Mensaje)>(p =>
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        UpdateProgress.Value = Math.Clamp(p.Porcentaje, 0, 99);
                        UpdateBar.Message = p.Mensaje;
                    });
                });

            var ruta = await ActualizacionProgramaService.DescargarPaqueteAsync(componente.Remoto, progreso);
            AreaAppLauncher.ForceClose(appId);
            var destino = AreaAppLauncher.InstallFolder(appId);
            if (ruta.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                ActualizacionProgramaService.LanzarInstalador(ruta);
                UpdateProgress.Visibility = Visibility.Collapsed;
                UpdateBar.IsOpen = false;
                await ShowAppDialog(name, "Se abrió el instalador. Cuando termine, vuelve a intentar abrir la app.");
                return;
            }

            ActualizacionProgramaService.AplicarPaquete(ruta, destino);
            _appsConActualizacion.Remove(appId);
            if (Session.Current is not null)
            {
                PintarTarjetas(Session.Current);
            }

            UpdateProgress.Value = 100;
            UpdateProgress.Visibility = Visibility.Collapsed;
            UpdateBar.Severity = InfoBarSeverity.Success;
            UpdateBar.Title = name;
            UpdateBar.Message = $"{name} quedó instalada.";
            UpdateBar.IsOpen = true;

            if (!AreaAppLauncher.IsInstalled(appId))
            {
                await ShowAppDialog(
                    name,
                    "Se descargó el paquete, pero no se encontró el ejecutable. Revisa que el .zip incluya el .exe de la app.");
                return;
            }

            var listo = new ContentDialog
            {
                Title = name,
                Content = $"{name} ya está lista. ¿Abrirla ahora?",
                PrimaryButtonText = "Abrir",
                CloseButtonText = "Cerrar",
                DefaultButton = ContentDialogButton.Primary
            };
            if (await MostrarDialogoAsync(listo) == ContentDialogResult.Primary)
            {
                var error = AreaAppLauncher.Launch(appId);
                if (!string.IsNullOrEmpty(error) && error != AreaAppLauncher.AlreadyRunning)
                {
                    await ShowAppDialog(name, error);
                }
            }
        }
        catch (Exception ex)
        {
            UpdateProgress.Visibility = Visibility.Collapsed;
            UpdateBar.Severity = InfoBarSeverity.Error;
            UpdateBar.Title = $"No se pudo {accion}";
            UpdateBar.Message = ex.Message;
            UpdateBar.IsOpen = true;
            await ShowAppDialog(name, $"No se pudo {accion} {name}.\n{ex.Message}");
        }
    }

    private async Task ShowAppDialog(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            PrimaryButtonText = "Cerrar"
        };
        await MostrarDialogoAsync(dialog);
    }

    private async Task<ContentDialogResult> MostrarDialogoAsync(ContentDialog dialog)
    {
        if (XamlRoot is null)
        {
            return ContentDialogResult.None;
        }

        await _dialogoLock.WaitAsync();
        try
        {
            dialog.XamlRoot = XamlRoot;
            return await dialog.ShowAsync();
        }
        finally
        {
            _dialogoLock.Release();
        }
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

        // Esperar a que la página tenga XamlRoot estable; no abrir ContentDialog solo.
        await Task.Delay(400);
        if (!IsLoaded || XamlRoot is null)
        {
            return;
        }

        if (await BuscarActualizacionAsync(preguntar: false))
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

    private async void DownloadAllButton_Click(object sender, RoutedEventArgs e)
    {
        await DescargarTodasLasAppsAsync();
    }

    /// <summary>
    /// Descarga e instala (o actualiza) todas las apps habilitadas para la cuenta actual.
    /// </summary>
    private async Task DescargarTodasLasAppsAsync()
    {
        if (_ocupado)
        {
            return;
        }

        var user = Session.Current;
        if (user is null)
        {
            return;
        }

        var habilitadas = user.AppIds
            .Select(AppCatalog.Find)
            .Where(app => app is not null
                          && !string.IsNullOrWhiteSpace(app.GitHubRepo)
                          && AreaAppLauncher.TryGetTarget(app.Id, out _))
            .Select(app => app!)
            .ToList();

        if (habilitadas.Count == 0)
        {
            await ShowAppDialog(
                "Descargar todo",
                "Su cuenta no tiene aplicaciones con descarga disponible.");
            return;
        }

        _ocupado = true;
        DownloadAllButton.IsEnabled = false;
        UpdatesButton.IsEnabled = false;
        UpdateBar.ActionButton = null;
        UpdateBar.IsOpen = false;
        UpdateProgress.Visibility = Visibility.Collapsed;
        MostrarEstadoTexto("Preparando descarga de sus aplicaciones…", alDia: false);

        try
        {
            using var limite = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            var pendientes = new List<ComponenteActualizable>();
            var sinPaquete = new List<string>();

            foreach (var app in habilitadas)
            {
                limite.Token.ThrowIfCancellationRequested();
                var local = AreaAppLauncher.VersionInstalada(app.Id) ?? new Version(0, 0, 0, 0);
                var item = await ActualizacionProgramaService.ConsultarComponenteAsync(
                    app.Id,
                    app.Name,
                    app.GitHubRepo,
                    local,
                    limite.Token);
                if (item is null && !AreaAppLauncher.IsInstalled(app.Id))
                {
                    item = await ActualizacionProgramaService.ConsultarComponenteAsync(
                        app.Id,
                        app.Name,
                        app.GitHubRepo,
                        new Version(0, 0, 0, 0),
                        limite.Token);
                }

                if (item is not null)
                {
                    pendientes.Add(item);
                }
                else if (!AreaAppLauncher.IsInstalled(app.Id))
                {
                    sinPaquete.Add(app.Name);
                }
            }

            if (pendientes.Count == 0)
            {
                UpdateStatusText.Visibility = Visibility.Collapsed;
                if (sinPaquete.Count > 0)
                {
                    await ShowAppDialog(
                        "Descargar todo",
                        "No hay paquetes publicados todavía para:\n• "
                        + string.Join("\n• ", sinPaquete));
                }
                else
                {
                    MostrarEstadoTexto("Ya tiene instaladas las aplicaciones de su cuenta", alDia: true);
                    await ShowAppDialog(
                        "Descargar todo",
                        "Ya tiene instaladas (y al día) todas las aplicaciones de su cuenta.");
                }

                // Aunque no haya apps pendientes, puede haber update del launcher.
                await OfrecerActualizarLauncherAsync(limite.Token);
                return;
            }

            UpdateStatusText.Visibility = Visibility.Collapsed;
            var lista = string.Join(
                "\n",
                pendientes.Select(p =>
                {
                    var accion = AreaAppLauncher.IsInstalled(p.Id) ? "actualizar" : "instalar";
                    return $"• {p.Nombre} ({accion}) → {p.Remoto.Etiqueta}{ActualizacionProgramaService.Tamano(p.Remoto)}";
                }));
            var extra = sinPaquete.Count > 0
                ? $"\n\nSin paquete publicado (se omiten):\n• {string.Join("\n• ", sinPaquete)}"
                : string.Empty;

            var dialogo = new ContentDialog
            {
                Title = "Descargar aplicaciones de su cuenta",
                Content =
                    $"Se instalarán primero las {pendientes.Count} app(s) de su cuenta:\n\n{lista}{extra}\n\n"
                    + "El launcher, si hay actualización, se ofrecerá al final (reinicia la ventana).",
                PrimaryButtonText = "Descargar e instalar",
                CloseButtonText = "Cancelar",
                DefaultButton = ContentDialogButton.Primary
            };
            if (await MostrarDialogoAsync(dialogo) != ContentDialogResult.Primary)
            {
                MostrarEstadoTexto(string.Empty, alDia: false);
                UpdateStatusText.Visibility = Visibility.Collapsed;
                return;
            }

            // Solo apps: el launcher se ofrece después, sin bloquear esta instalación.
            await DescargarEInstalarAsync(pendientes, confirmar: false, tituloExito: "Apps instaladas");
            await OfrecerActualizarLauncherAsync(limite.Token);
        }
        catch (Exception ex)
        {
            UpdateStatusText.Visibility = Visibility.Collapsed;
            UpdateBar.ActionButton = null;
            UpdateBar.Severity = InfoBarSeverity.Error;
            UpdateBar.Title = "No se pudo descargar";
            UpdateBar.Message = ex.Message;
            UpdateBar.IsOpen = true;
        }
        finally
        {
            _ocupado = false;
            DownloadAllButton.IsEnabled = true;
            UpdatesButton.IsEnabled = true;
        }
    }

    private async Task<bool> BuscarActualizacionAsync(bool preguntar)
    {
        if (_ocupado)
        {
            return false;
        }

        var user = Session.Current;
        if (user is null)
        {
            return false;
        }

        _ocupado = true;
        UpdatesButton.IsEnabled = false;
        UpdateBar.ActionButton = null;
        UpdateBar.IsOpen = false;
        UpdateProgress.Visibility = Visibility.Collapsed;
        MostrarEstadoTexto("Buscando actualizaciones de sus aplicaciones…", alDia: false);

        try
        {
            using var limite = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var pendientes = await ActualizacionProgramaService.ConsultarParaUsuarioAsync(
                user.AppIds,
                AreaAppLauncher.VersionInstalada,
                limite.Token);

            if (pendientes.Count == 0)
            {
                UpdateBar.IsOpen = false;
                MostrarEstadoTexto("Sus aplicaciones están actualizadas", alDia: true);
                return true;
            }

            UpdateStatusText.Visibility = Visibility.Collapsed;
            var resumen = ActualizacionProgramaService.Resumen(pendientes);
            var accion = new Button { Content = "Descargar e instalar" };
            accion.Click += async (_, _) => await DescargarEInstalarAsync(pendientes);
            UpdateBar.ActionButton = accion;
            UpdateBar.Severity = InfoBarSeverity.Warning;
            UpdateBar.Title = pendientes.Count == 1
                ? "Hay una actualización"
                : $"Hay {pendientes.Count} actualizaciones";
            UpdateBar.Message =
                $"Según sus permisos: {resumen}. Solo se actualizan el launcher y las apps que tiene habilitadas.";
            UpdateBar.IsOpen = true;

            if (preguntar)
            {
                // Fuera del InfoBar: el usuario pulsó Actualizaciones a propósito.
                await DescargarEInstalarAsync(pendientes);
            }
        }
        catch (Exception ex)
        {
            UpdateStatusText.Visibility = Visibility.Collapsed;
            UpdateBar.ActionButton = null;
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

    private static bool EsLauncher(ComponenteActualizable p) =>
        p.Id.Equals(ActualizacionProgramaService.IdLauncher, StringComparison.OrdinalIgnoreCase);

    private static List<ComponenteActualizable> SoloApps(IEnumerable<ComponenteActualizable> pendientes) =>
        pendientes
            .Where(p => !EsLauncher(p))
            .OrderBy(p => p.Nombre, StringComparer.Create(new System.Globalization.CultureInfo("es-ES"), ignoreCase: true))
            .ToList();

    private static ComponenteActualizable? SoloLauncher(IEnumerable<ComponenteActualizable> pendientes) =>
        pendientes.FirstOrDefault(EsLauncher);

    /// <summary>
    /// Tras instalar apps: pregunta si actualizar el launcher (reinicia). No bloquea el uso de las apps.
    /// </summary>
    private async Task OfrecerActualizarLauncherAsync(CancellationToken cancelar = default)
    {
        if (!IsLoaded || XamlRoot is null)
        {
            return;
        }

        ComponenteActualizable? launcher;
        try
        {
            launcher = await ActualizacionProgramaService.ConsultarComponenteAsync(
                ActualizacionProgramaService.IdLauncher,
                "Launcher",
                AppRepos.Launcher,
                ActualizacionProgramaService.VersionInstalada,
                cancelar);
        }
        catch
        {
            return;
        }

        if (launcher is null)
        {
            return;
        }

        var dialogo = new ContentDialog
        {
            Title = "Actualizar launcher",
            Content =
                $"Las aplicaciones ya pueden usarse.\n\n"
                + $"Hay una versión nueva del launcher ({launcher.Remoto.Etiqueta}"
                + $"{ActualizacionProgramaService.Tamano(launcher.Remoto)}).\n"
                + "Si la aplica ahora, esta ventana se reiniciará.",
            PrimaryButtonText = "Actualizar y reiniciar",
            CloseButtonText = "Más tarde",
            DefaultButton = ContentDialogButton.Close
        };
        if (await MostrarDialogoAsync(dialogo) != ContentDialogResult.Primary)
        {
            UpdateBar.Severity = InfoBarSeverity.Informational;
            UpdateBar.Title = "Launcher pendiente";
            UpdateBar.Message = "Puede actualizar el launcher cuando quiera desde Actualizaciones.";
            UpdateBar.ActionButton = null;
            UpdateBar.IsOpen = true;
            return;
        }

        await AplicarLauncherAsync(launcher);
    }

    private async Task AplicarLauncherAsync(ComponenteActualizable launcher)
    {
        UpdatesButton.IsEnabled = false;
        DownloadAllButton.IsEnabled = false;
        UpdateBar.ActionButton = null;
        UpdateBar.Severity = InfoBarSeverity.Informational;
        UpdateBar.Title = "Launcher";
        UpdateBar.Message = "Descargando launcher…";
        UpdateBar.IsOpen = true;
        UpdateProgress.Visibility = Visibility.Visible;
        UpdateProgress.Value = 0;
        var progresoActivo = true;

        try
        {
            IProgress<(double Porcentaje, string Mensaje)> progreso =
                new Progress<(double Porcentaje, string Mensaje)>(p =>
                {
                    if (!progresoActivo)
                    {
                        return;
                    }

                    DispatcherQueue.TryEnqueue(() =>
                    {
                        if (!progresoActivo)
                        {
                            return;
                        }

                        UpdateProgress.Value = Math.Clamp(p.Porcentaje, 0, 99);
                        UpdateBar.Message = p.Mensaje;
                    });
                });

            var ruta = await ActualizacionProgramaService.DescargarPaqueteAsync(launcher.Remoto, progreso);
            progresoActivo = false;

            if (ruta.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                ActualizacionProgramaService.LanzarInstalador(ruta);
                UpdateBar.Severity = InfoBarSeverity.Success;
                UpdateBar.Title = "Instalador del launcher";
                UpdateBar.Message = "Se abrió el instalador. Cuando termine, reinicie el launcher.";
                UpdateProgress.Visibility = Visibility.Collapsed;
                return;
            }

            var exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
            {
                throw new InvalidOperationException("No se encontró el ejecutable del launcher.");
            }

            var carpetaLauncher = Path.GetDirectoryName(exe) ?? AppContext.BaseDirectory;
            // No copiar encima del proceso en ejecución: script tras cerrar.
            ActualizacionProgramaService.AplicarLauncherTrasCerrar(
                ruta,
                carpetaLauncher,
                exe,
                Environment.ProcessId);

            UpdateProgress.Value = 100;
            UpdateBar.Severity = InfoBarSeverity.Success;
            UpdateBar.Title = "Launcher";
            UpdateBar.Message = "Cerrando para aplicar la actualización…";
            UpdateProgress.Visibility = Visibility.Collapsed;
            App.MainAppWindow?.CerrarParaActualizar();
        }
        catch (Exception ex)
        {
            progresoActivo = false;
            UpdateBar.Severity = InfoBarSeverity.Error;
            UpdateBar.Title = "No se pudo actualizar el launcher";
            UpdateBar.Message = string.IsNullOrWhiteSpace(ex.Message)
                ? "Error al aplicar el paquete."
                : ex.Message;
            UpdateProgress.Visibility = Visibility.Collapsed;
        }
        finally
        {
            UpdatesButton.IsEnabled = true;
            DownloadAllButton.IsEnabled = true;
        }
    }

    private async Task DescargarEInstalarAsync(
        IReadOnlyList<ComponenteActualizable> pendientes,
        bool confirmar = true,
        string tituloExito = "Actualización aplicada")
    {
        if (!IsLoaded || pendientes.Count == 0)
        {
            return;
        }

        // Las apps se instalan completas; el launcher (si viene en la lista) se ofrece después.
        var apps = SoloApps(pendientes);
        var launcher = SoloLauncher(pendientes);

        if (apps.Count == 0 && launcher is not null)
        {
            if (confirmar)
            {
                await OfrecerActualizarLauncherAsync();
            }
            else
            {
                await AplicarLauncherAsync(launcher);
            }

            return;
        }

        if (apps.Count == 0)
        {
            return;
        }

        if (confirmar)
        {
            var lista = string.Join("\n", apps.Select(p =>
                $"• {p.Nombre}: {p.VersionLocal} → {p.Remoto.Etiqueta}{ActualizacionProgramaService.Tamano(p.Remoto)}"));
            var notaLauncher = launcher is not null
                ? "\n\nEl launcher se ofrecerá al final (reinicia la ventana); no interrumpe la instalación de las apps."
                : string.Empty;
            var dialogo = new ContentDialog
            {
                Title = "Actualizar sus aplicaciones",
                Content =
                    $"Se instalarán primero las apps habilitadas:\n\n{lista}{notaLauncher}\n\n"
                    + "Se cerrarán las áreas que se actualicen.",
                PrimaryButtonText = "Descargar e instalar",
                CloseButtonText = "Más tarde",
                DefaultButton = ContentDialogButton.Primary
            };
            if (await MostrarDialogoAsync(dialogo) != ContentDialogResult.Primary)
            {
                return;
            }
        }

        UpdatesButton.IsEnabled = false;
        DownloadAllButton.IsEnabled = false;
        UpdateBar.ActionButton = null;
        UpdateBar.Severity = InfoBarSeverity.Informational;
        UpdateBar.Title = "Descargando apps…";
        UpdateBar.Message = "Preparando paquetes…";
        UpdateBar.IsOpen = true;
        UpdateProgress.Visibility = Visibility.Visible;
        UpdateProgress.Value = 0;
        try
        {
            for (var i = 0; i < apps.Count; i++)
            {
                var item = apps[i];
                var basePct = 100.0 * i / apps.Count;
                UpdateBar.Title = $"{item.Nombre} ({i + 1}/{apps.Count})";
                IProgress<(double Porcentaje, string Mensaje)> progreso =
                    new Progress<(double Porcentaje, string Mensaje)>(p =>
                    {
                        UpdateProgress.Value = Math.Clamp(basePct + p.Porcentaje / apps.Count, 0, 99);
                        UpdateBar.Message = $"{item.Nombre}: {p.Mensaje}";
                    });

                var ruta = await ActualizacionProgramaService.DescargarPaqueteAsync(item.Remoto, progreso);
                AreaAppLauncher.ForceClose(item.Id);
                var carpeta = AreaAppLauncher.InstallFolder(item.Id);
                if (ruta.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    ActualizacionProgramaService.LanzarInstalador(ruta);
                }
                else
                {
                    ActualizacionProgramaService.AplicarPaquete(ruta, carpeta);
                }

                _appsConActualizacion.Remove(item.Id);
            }

            if (Session.Current is not null)
            {
                PintarTarjetas(Session.Current);
            }

            UpdateProgress.Value = 100;
            UpdateBar.Severity = InfoBarSeverity.Success;
            UpdateBar.Title = tituloExito;
            UpdateBar.Message = $"Listo: {apps.Count} aplicación(es). Ya puede usarlas.";
            UpdateBar.IsOpen = true;
            UpdateProgress.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            UpdateBar.Severity = InfoBarSeverity.Error;
            UpdateBar.Title = "No se pudo completar";
            UpdateBar.Message = ex.Message;
            UpdateProgress.Visibility = Visibility.Collapsed;
            return;
        }
        finally
        {
            UpdatesButton.IsEnabled = true;
            DownloadAllButton.IsEnabled = true;
        }

        // Apps terminadas: recién ahora el launcher (opcional / con reinicio).
        if (launcher is not null)
        {
            await OfrecerActualizarLauncherAsync();
        }
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
