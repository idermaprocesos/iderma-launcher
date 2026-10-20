using System.Runtime.InteropServices;
using Iderma.Core;
using Iderma.Core.Catalog;
using Iderma.Core.Models;
using Iderma.Core.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Iderma_Launcher;

public sealed partial class MainWindow : Window
{
    private readonly DispatcherTimer _idleTimer;
    private DateTime _lastActivityUtc = DateTime.UtcNow;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppTitleBar.Title = AppInfo.ProductName;
        Title = AppInfo.ProductName;
        Activated += OnFirstActivated;

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.Maximize();
        }

        RootHost.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler((_, _) => NoteActivity()), true);
        RootHost.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, _) => NoteActivity()), true);
        RootHost.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler((_, _) => NoteActivity()), true);

        _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _idleTimer.Tick += (_, _) => EvaluateIdleLock();
        _idleTimer.Start();

        RootFrame.Navigated += (_, _) =>
        {
            if (_guiaEnCurso && !_navegacionGuia)
            {
                TerminarGuia();
            }
        };
        RootFrame.Navigate(typeof(LoginPage));
        Closed += (_, _) => TaskbarJumpList.Clear();
    }

    public void LaunchFromJumpList(string appId)
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => LaunchFromJumpList(appId));
            return;
        }

        _ = LaunchFromJumpListAsync(appId);
    }

    private async Task LaunchFromJumpListAsync(string appId)
    {
        if (RootFrame.Content is LoginPage or SessionLockPage)
        {
            return;
        }

        var user = Session.Current;
        var app = AppCatalog.Find(appId);
        if (user is null || app is null || app.IsLocked
            || !user.AppIds.Contains(app.Id, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            if (!AreaAppLauncher.IsInstalled(app.Id))
            {
                await ShowJumpListDialog(
                    app.Name,
                    $"{app.Name} no está instalada. Ábrela desde el launcher para descargarla.");
                return;
            }

            var error = AreaAppLauncher.Launch(app.Id);
            if (string.IsNullOrEmpty(error))
            {
                return;
            }

            if (error == AreaAppLauncher.AlreadyRunning)
            {
                await RunningAppDialog.ShowAsync(RootHost.XamlRoot, app.Id, app.Name);
                return;
            }

            await ShowJumpListDialog(app.Name, error);
        }
        catch (Exception ex)
        {
            await ShowJumpListDialog(app.Name, $"No se pudo abrir {app.Name}.\n{ex.Message}");
        }
    }

    private async Task ShowJumpListDialog(string title, string message)
    {
        if (RootHost.XamlRoot is null)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            PrimaryButtonText = "Cerrar",
            XamlRoot = RootHost.XamlRoot
        };
        await dialog.ShowAsync();
    }

    public void SetHeaderTitle(string title) => AppTitleBar.Title = title;

    private bool _guiaEnCurso;
    private bool _navegacionGuia;
    private bool _avanzandoGuia;
    private int _indiceGuia = -1;
    private int _secuenciaGuia;

    public async void IniciarGuiaInicioRapido()
    {
        _secuenciaGuia++;
        _guiaEnCurso = true;
        _indiceGuia = 0;
        await MostrarPasoGuiaAsync();
    }

    private async void TipGuia_ActionButtonClick(TeachingTip sender, object args)
    {
        if (!_guiaEnCurso || _avanzandoGuia)
        {
            return;
        }

        if (_indiceGuia >= GuiaInicioRapido.Pasos.Count - 1)
        {
            TerminarGuia();
            return;
        }

        _indiceGuia++;
        await MostrarPasoGuiaAsync();
    }

    private void TipGuia_CloseButtonClick(TeachingTip sender, object args)
    {
        if (_avanzandoGuia)
        {
            return;
        }

        TerminarGuia();
    }

    private void TerminarGuia()
    {
        _guiaEnCurso = false;
        _indiceGuia = -1;
        _secuenciaGuia++;
        _avanzandoGuia = false;
        if (TipGuia.IsOpen)
        {
            TipGuia.IsOpen = false;
        }
    }

    private async Task MostrarPasoGuiaAsync()
    {
        if (!_guiaEnCurso || _indiceGuia < 0)
        {
            TerminarGuia();
            return;
        }

        var secuencia = ++_secuenciaGuia;
        _avanzandoGuia = true;
        try
        {
            while (_guiaEnCurso && _indiceGuia < GuiaInicioRapido.Pasos.Count)
            {
                var candidato = GuiaInicioRapido.Pasos[_indiceGuia];
                if (!candidato.Opcional)
                {
                    break;
                }

                IrA(candidato.Pagina);
                await EsperarPaginaGuiaAsync();
                if (secuencia != _secuenciaGuia || !_guiaEnCurso)
                {
                    return;
                }

                if (EsObjetivoGuiaUsable(BuscarObjetivoGuia(candidato.Nombre)))
                {
                    break;
                }

                _indiceGuia++;
            }

            if (!_guiaEnCurso || _indiceGuia < 0 || _indiceGuia >= GuiaInicioRapido.Pasos.Count)
            {
                TerminarGuia();
                return;
            }

            var indice = _indiceGuia;
            var paso = GuiaInicioRapido.Pasos[indice];
            await CerrarTipGuiaAsync();
            if (secuencia != _secuenciaGuia || !_guiaEnCurso)
            {
                return;
            }

            TipGuia.Target = null;
            IrA(paso.Pagina);
            await EsperarPaginaGuiaAsync();
            await Task.Delay(120);
            if (secuencia != _secuenciaGuia || !_guiaEnCurso)
            {
                return;
            }

            var objetivo = BuscarObjetivoGuia(paso.Nombre) ?? AnclaGuia;
            if (objetivo.ActualWidth <= 0 || objetivo.ActualHeight <= 0)
            {
                objetivo.UpdateLayout();
            }

            objetivo.StartBringIntoView(new BringIntoViewOptions
            {
                AnimationDesired = false,
                VerticalAlignmentRatio = 0.2
            });
            await Task.Delay(80);
            if (secuencia != _secuenciaGuia || !_guiaEnCurso)
            {
                return;
            }

            if (!EsObjetivoGuiaUsable(objetivo))
            {
                objetivo = AnclaGuia;
            }

            TipGuia.Title = paso.Titulo;
            TipGuia.Subtitle = paso.Texto;
            TipGuia.PreferredPlacement = paso.Lugar switch
            {
                LugarGuia.Abajo => TeachingTipPlacementMode.Bottom,
                LugarGuia.Arriba => TeachingTipPlacementMode.Top,
                _ => TeachingTipPlacementMode.Auto
            };
            TipGuia.ActionButtonContent = indice >= GuiaInicioRapido.Pasos.Count - 1
                ? "Terminar"
                : "Siguiente";
            TipGuia.CloseButtonContent = "Salir";
            TipGuia.Target = objetivo;
            await Task.Delay(16);
            if (secuencia != _secuenciaGuia || !_guiaEnCurso)
            {
                return;
            }

            TipGuia.IsOpen = true;
        }
        catch
        {
            if (secuencia == _secuenciaGuia && _guiaEnCurso && _indiceGuia >= 0 && _indiceGuia < GuiaInicioRapido.Pasos.Count)
            {
                var paso = GuiaInicioRapido.Pasos[_indiceGuia];
                TipGuia.Title = paso.Titulo;
                TipGuia.Subtitle = paso.Texto;
                TipGuia.Target = AnclaGuia;
                TipGuia.PreferredPlacement = TeachingTipPlacementMode.Bottom;
                TipGuia.ActionButtonContent = "Siguiente";
                TipGuia.CloseButtonContent = "Salir";
                TipGuia.IsOpen = true;
            }
        }
        finally
        {
            if (secuencia == _secuenciaGuia)
            {
                _avanzandoGuia = false;
            }
        }
    }

    private void IrA(PaginaGuia pagina)
    {
        var destino = pagina == PaginaGuia.Inicio ? typeof(HomePage) : typeof(SettingsPage);
        if (RootFrame.Content?.GetType() == destino)
        {
            return;
        }

        _navegacionGuia = true;
        try
        {
            RootFrame.Navigate(destino);
        }
        finally
        {
            _navegacionGuia = false;
        }
    }

    private async Task EsperarPaginaGuiaAsync()
    {
        if (RootFrame.Content is FrameworkElement pagina && !pagina.IsLoaded)
        {
            var cargada = new TaskCompletionSource();
            void AlCargar(object sender, RoutedEventArgs args)
            {
                pagina.Loaded -= AlCargar;
                cargada.TrySetResult();
            }

            pagina.Loaded += AlCargar;
            await Task.WhenAny(cargada.Task, Task.Delay(800));
            pagina.Loaded -= AlCargar;
        }
    }

    private FrameworkElement? BuscarObjetivoGuia(string nombre) =>
        (RootFrame.Content as FrameworkElement)?.FindName(nombre) as FrameworkElement;

    private static bool EsObjetivoGuiaUsable(FrameworkElement? elemento) =>
        elemento is not null
        && elemento.IsLoaded
        && elemento.Visibility == Visibility.Visible
        && elemento.ActualWidth >= 8
        && elemento.ActualHeight >= 8;

    private async Task CerrarTipGuiaAsync()
    {
        if (!TipGuia.IsOpen)
        {
            return;
        }

        var cerrado = new TaskCompletionSource();
        void AlCerrar(TeachingTip sender, TeachingTipClosedEventArgs args)
        {
            sender.Closed -= AlCerrar;
            cerrado.TrySetResult();
        }

        TipGuia.Closed += AlCerrar;
        TipGuia.IsOpen = false;
        var timeout = Task.Delay(700);
        if (await Task.WhenAny(cerrado.Task, timeout) == timeout)
        {
            TipGuia.Closed -= AlCerrar;
        }
    }

    private IntPtr _iconBig;
    private IntPtr _iconSmall;

    private void OnFirstActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated)
        {
            return;
        }

        Activated -= OnFirstActivated;
        DispatcherQueue.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            ApplyWindowIcon);
    }

    private bool _iconApplied;

    public void ApplyWindowIcon()
    {
        if (_iconApplied)
        {
            return;
        }

        _iconApplied = true;
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "taskbar.ico");
        if (!File.Exists(iconPath))
        {
            iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "launcher.ico");
        }

        if (!File.Exists(iconPath))
        {
            iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "logo.ico");
        }

        if (!File.Exists(iconPath))
        {
            return;
        }

        SHChangeNotify(ShcneUpdateItem, ShcnfPath, iconPath, IntPtr.Zero);
        AppWindow.SetIcon(iconPath);

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        EnsureIcons(iconPath);
        if (_iconSmall != IntPtr.Zero)
        {
            SendMessage(hwnd, WmSetIcon, (IntPtr)IconSmall, _iconSmall);
            SetClassIcon(hwnd, GclpHiconSm, _iconSmall);
        }

        if (_iconBig != IntPtr.Zero)
        {
            SendMessage(hwnd, WmSetIcon, (IntPtr)IconBig, _iconBig);
            SetClassIcon(hwnd, GclpHicon, _iconBig);
        }

        TaskbarIcon.Apply(hwnd, iconPath);
        var repairPath = iconPath;
        DispatcherQueue.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () => TaskbarIcon.RepairPinned(repairPath));
    }

    private void EnsureIcons(string iconPath)
    {
        if (_iconBig != IntPtr.Zero || _iconSmall != IntPtr.Zero)
        {
            return;
        }

        _iconBig = LoadImage(IntPtr.Zero, iconPath, ImageIcon, 256, 256, LoadFromFile);
        if (_iconBig == IntPtr.Zero)
        {
            _iconBig = LoadImage(IntPtr.Zero, iconPath, ImageIcon, 32, 32, LoadFromFile);
        }

        _iconSmall = LoadImage(IntPtr.Zero, iconPath, ImageIcon, 16, 16, LoadFromFile);
    }

    private static void SetClassIcon(IntPtr hwnd, int index, IntPtr icon)
    {
        if (IntPtr.Size == 8)
        {
            SetClassLongPtr64(hwnd, index, icon);
            return;
        }

        SetClassLong32(hwnd, index, icon.ToInt32());
    }

    private const uint WmSetIcon = 0x0080;
    private const uint ImageIcon = 1;
    private const uint LoadFromFile = 0x0010;
    private const int ShcneUpdateItem = 0x00002000;
    private const uint ShcnfPath = 0x0005;
    private const int IconSmall = 0;
    private const int IconBig = 1;
    private const int GclpHicon = -14;
    private const int GclpHiconSm = -34;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImage(IntPtr hInst, string name, uint type, int cx, int cy, uint load);

    [DllImport("user32.dll", EntryPoint = "SetClassLongPtrW", SetLastError = true)]
    private static extern IntPtr SetClassLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetClassLongW", SetLastError = true)]
    private static extern int SetClassLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(int eventId, uint flags, string item1, IntPtr item2);

    public void CerrarParaActualizar()
    {
        Close();
        // Asegura que el proceso termine para que el script pueda sobrescribir DLLs/EXE.
        Application.Current?.Exit();
    }

    public void NoteActivity()
    {
        if (RootFrame.Content is SessionLockPage)
        {
            return;
        }

        _lastActivityUtc = DateTime.UtcNow;
    }

    private void EvaluateIdleLock()
    {
        var user = Session.Current;
        if (user is null || RootFrame.Content is LoginPage or SessionLockPage)
        {
            return;
        }

        var minutes = App.Security.GetPolicy().IdleLockMinutes;
        if (minutes <= 0 || DateTime.UtcNow - _lastActivityUtc < TimeSpan.FromMinutes(minutes))
        {
            return;
        }

        App.Security.Record(SecurityEventKind.SessionLocked, user.Username, "Bloqueo por inactividad.");
        RootFrame.Navigate(typeof(SessionLockPage));
    }
}
