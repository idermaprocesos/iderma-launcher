using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;
using Windows.System;

namespace Iderma_Launcher;

public sealed partial class ArcadeWindow : Window
{
    private static ArcadeWindow? _instance;
    private static bool _mainHooked;

    private readonly DispatcherTimer _keepAlive;
    private Style? _accentTabStyle;
    private bool _ready;

    public ArcadeWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        Title = "Arcade";
        _accentTabStyle = MinesTabButton.Style;

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "taskbar.ico");
        if (!File.Exists(iconPath))
        {
            iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "launcher.ico");
        }
        if (!File.Exists(iconPath))
        {
            iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "logo.ico");
        }
        if (File.Exists(iconPath))
        {
            AppWindow.SetIcon(iconPath);
        }

        PlaceOnScreen();
        InitMineBrushes();
        InitSnakeBrushes();

        _mineTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _mineTimer.Tick += (_, _) => OnMineTick();
        _snakeTimer = new DispatcherTimer();
        _snakeTimer.Tick += (_, _) => OnSnakeTick();
        _keepAlive = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _keepAlive.Tick += (_, _) => App.MainAppWindow?.NoteActivity();

        Activated += OnActivated;
        Closed += (_, _) =>
        {
            _mineTimer.Stop();
            _snakeTimer.Stop();
            _keepAlive.Stop();
        };
        Root.Loaded += (_, _) =>
        {
            if (_ready)
            {
                return;
            }

            EnsureSnakeGrid();
            StartMines();
            ResetSnake();
            _ready = true;
        };
    }

    public static void Open()
    {
        if (_instance is not null)
        {
            _instance.Activate();
            return;
        }

        HookMainWindow();
        var window = new ArcadeWindow();
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_instance, window))
            {
                _instance = null;
            }
        };
        _instance = window;
        window.Activate();
    }

    private static void HookMainWindow()
    {
        if (_mainHooked || App.MainAppWindow is null)
        {
            return;
        }

        _mainHooked = true;
        App.MainAppWindow.Closed += (_, _) => _instance?.Close();
    }

    private void PlaceOnScreen()
    {
        const int preferredWidth = 980;
        const int preferredHeight = 760;
        var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var width = Math.Min(preferredWidth, Math.Max(480, work.Width - 32));
        var height = Math.Min(preferredHeight, Math.Max(420, work.Height - 32));
        AppWindow.Resize(new SizeInt32(width, height));
        AppWindow.Move(new PointInt32(
            work.X + Math.Max(0, (work.Width - width) / 2),
            work.Y + Math.Max(0, (work.Height - height) / 2)));
    }

    private void OnActivated(object sender, WindowActivatedEventArgs e)
    {
        if (e.WindowActivationState == WindowActivationState.Deactivated)
        {
            _keepAlive.Stop();
            PauseSnake();
            return;
        }

        App.MainAppWindow?.NoteActivity();
        _keepAlive.Start();
    }

    private void MinesTab_Click(object sender, RoutedEventArgs e) => UseSnake(false);

    private void SnakeTab_Click(object sender, RoutedEventArgs e) => UseSnake(true);

    private void UseSnake(bool snake)
    {
        MinesPanel.Visibility = snake ? Visibility.Collapsed : Visibility.Visible;
        SnakePanel.Visibility = snake ? Visibility.Visible : Visibility.Collapsed;
        if (snake)
        {
            MinesTabButton.ClearValue(FrameworkElement.StyleProperty);
            SnakeTabButton.Style = _accentTabStyle;
            if (_mineStarted && !_mineOver)
            {
                _mineTimer.Stop();
            }

            FocusSnake();
            return;
        }

        SnakeTabButton.ClearValue(FrameworkElement.StyleProperty);
        MinesTabButton.Style = _accentTabStyle;

        if (_snakeRunning && !_snakePaused)
        {
            PauseSnake();
        }

        if (_mineStarted && !_mineOver)
        {
            _mineTimer.Start();
        }
    }

    private void Root_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!_ready || SnakePanel.Visibility != Visibility.Visible || e.KeyStatus.WasKeyDown)
        {
            return;
        }

        var focused = Content.XamlRoot is null ? null : FocusManager.GetFocusedElement(Content.XamlRoot);
        if (focused is ComboBox or ComboBoxItem)
        {
            return;
        }

        if (e.Key == VirtualKey.Space)
        {
            if (focused is Button)
            {
                return;
            }

            ToggleSnakePause();
            e.Handled = true;
            return;
        }

        if (TryTurn(e.Key))
        {
            e.Handled = true;
        }
    }
}
