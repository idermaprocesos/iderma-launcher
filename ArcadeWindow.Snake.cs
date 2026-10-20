using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI;

namespace Iderma_Launcher;

public sealed partial class ArcadeWindow
{
    private const int SnakeN = 20;
    private const double SnakeCell = 22;

    private static int _snakeBest;

    private DispatcherTimer _snakeTimer = null!;
    private Border[,]? _snakeViews;
    private List<(int X, int Y)> _snake = [];
    private (int X, int Y) _dir = (1, 0);
    private (int X, int Y)? _buffered;
    private (int X, int Y) _food;
    private int _snakeScore;
    private bool _snakeRunning;
    private bool _snakePaused;
    private bool _snakeOver;
    private bool _snakeBuilt;

    private SolidColorBrush _snakeEmpty = null!;
    private SolidColorBrush _snakeBody = null!;
    private SolidColorBrush _snakeHead = null!;
    private SolidColorBrush _snakeFood = null!;

    private void InitSnakeBrushes()
    {
        _snakeEmpty = BrushOf(0xFF22262E);
        _snakeBody = BrushOf(0xFF3DDC97);
        _snakeHead = BrushOf(0xFFB9F6CA);
        _snakeFood = BrushOf(0xFFFF5252);
    }

    private void EnsureSnakeGrid()
    {
        if (_snakeBuilt)
        {
            return;
        }

        _snakeBuilt = true;
        _snakeViews = new Border[SnakeN, SnakeN];
        SnakeGrid.RowDefinitions.Clear();
        SnakeGrid.ColumnDefinitions.Clear();
        SnakeGrid.Children.Clear();
        for (var i = 0; i < SnakeN; i++)
        {
            SnakeGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(SnakeCell) });
            SnakeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(SnakeCell) });
        }

        for (var y = 0; y < SnakeN; y++)
        {
            for (var x = 0; x < SnakeN; x++)
            {
                var cell = new Border
                {
                    Background = _snakeEmpty,
                    CornerRadius = new CornerRadius(3),
                    Margin = new Thickness(1),
                };
                Grid.SetRow(cell, y);
                Grid.SetColumn(cell, x);
                SnakeGrid.Children.Add(cell);
                _snakeViews[y, x] = cell;
            }
        }
    }

    private void SnakeDifficulty_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready)
        {
            return;
        }

        ApplySnakeSpeed();
    }

    private void SnakeRestart_Click(object sender, RoutedEventArgs e) => ResetSnake();

    private void SnakeOverlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (_snakePaused && !_snakeOver)
        {
            ResumeSnake();
            return;
        }

        ResetSnake();
    }

    private void ResetSnake()
    {
        _snakeTimer.Stop();
        _snakeOver = false;
        _snakePaused = false;
        _snakeRunning = false;
        _snakeScore = 0;
        _dir = (1, 0);
        _buffered = null;
        var mid = SnakeN / 2;
        _snake = [(mid, mid), (mid - 1, mid), (mid - 2, mid)];
        SpawnFood();
        ApplySnakeSpeed();
        DrawSnake();
        UpdateSnakeHud();
        SnakeOverlay.Visibility = Visibility.Collapsed;
        SnakeHint.Text = "Pulsa una flecha o WASD para empezar.";
        FocusSnake();
    }

    private void FocusSnake()
    {
        SnakeBoard.Focus(FocusState.Programmatic);
    }

    private bool TryTurn(VirtualKey key)
    {
        (int X, int Y)? dir = key switch
        {
            VirtualKey.Up or VirtualKey.W => (0, -1),
            VirtualKey.Down or VirtualKey.S => (0, 1),
            VirtualKey.Left or VirtualKey.A => (-1, 0),
            VirtualKey.Right or VirtualKey.D => (1, 0),
            _ => null,
        };
        if (dir is null)
        {
            return false;
        }

        QueueDirection(dir.Value.X, dir.Value.Y);
        return true;
    }

    private void QueueDirection(int x, int y)
    {
        if (_snakeOver || (x == -_dir.X && y == -_dir.Y))
        {
            return;
        }

        if (!_snakeRunning)
        {
            _dir = (x, y);
            _buffered = null;
            BeginSnake();
            return;
        }

        _buffered = (x, y);
        if (_snakePaused)
        {
            ResumeSnake();
        }
    }

    private void BeginSnake()
    {
        _snakeRunning = true;
        _snakePaused = false;
        SnakeOverlay.Visibility = Visibility.Collapsed;
        SnakeHint.Text = "Flechas o WASD para moverte. Espacio para pausar.";
        ApplySnakeSpeed();
        _snakeTimer.Start();
        App.MainAppWindow?.NoteActivity();
    }

    private void ToggleSnakePause()
    {
        if (!_snakeRunning || _snakeOver)
        {
            return;
        }

        if (_snakePaused)
        {
            ResumeSnake();
            return;
        }

        PauseSnake();
    }

    private void PauseSnake()
    {
        if (!_snakeRunning || _snakePaused || _snakeOver)
        {
            return;
        }

        _snakePaused = true;
        _snakeTimer.Stop();
        ShowSnakeOverlay("Pausa", "Espacio para continuar.", "Continuar");
    }

    private void ResumeSnake()
    {
        if (!_snakePaused || _snakeOver)
        {
            return;
        }

        _snakePaused = false;
        SnakeOverlay.Visibility = Visibility.Collapsed;
        _snakeTimer.Start();
        FocusSnake();
    }

    private void OnSnakeTick()
    {
        if (!_snakeRunning || _snakePaused || _snakeOver)
        {
            return;
        }

        if (_buffered is { } turn && !(turn.X == -_dir.X && turn.Y == -_dir.Y))
        {
            _dir = turn;
        }

        _buffered = null;
        var head = _snake[0];
        var next = (X: head.X + _dir.X, Y: head.Y + _dir.Y);
        if (next.X < 0 || next.Y < 0 || next.X >= SnakeN || next.Y >= SnakeN)
        {
            EndSnake(cleared: false);
            return;
        }

        var eating = next.X == _food.X && next.Y == _food.Y;
        var limit = _snake.Count - (eating ? 0 : 1);
        for (var i = 0; i < limit; i++)
        {
            if (_snake[i].X == next.X && _snake[i].Y == next.Y)
            {
                EndSnake(cleared: false);
                return;
            }
        }

        _snake.Insert(0, next);
        if (eating)
        {
            _snakeScore++;
            if (_snakeScore > _snakeBest)
            {
                _snakeBest = _snakeScore;
            }

            UpdateSnakeHud();
            ApplySnakeSpeed();
            if (!SpawnFood())
            {
                DrawSnake();
                EndSnake(cleared: true);
                return;
            }
        }
        else
        {
            _snake.RemoveAt(_snake.Count - 1);
        }

        DrawSnake();
    }

    private bool SpawnFood()
    {
        var taken = new HashSet<(int X, int Y)>(_snake);
        var free = new List<(int X, int Y)>();
        for (var y = 0; y < SnakeN; y++)
        {
            for (var x = 0; x < SnakeN; x++)
            {
                if (!taken.Contains((x, y)))
                {
                    free.Add((x, y));
                }
            }
        }

        if (free.Count == 0)
        {
            return false;
        }

        _food = free[Random.Shared.Next(free.Count)];
        return true;
    }

    private void DrawSnake()
    {
        if (_snakeViews is null)
        {
            return;
        }

        for (var y = 0; y < SnakeN; y++)
        {
            for (var x = 0; x < SnakeN; x++)
            {
                _snakeViews[y, x].Background = _snakeEmpty;
            }
        }

        for (var i = 0; i < _snake.Count; i++)
        {
            var part = _snake[i];
            _snakeViews[part.Y, part.X].Background = i == 0 ? _snakeHead : _snakeBody;
        }

        if (_snake.Count < SnakeN * SnakeN)
        {
            _snakeViews[_food.Y, _food.X].Background = _snakeFood;
        }
    }

    private void EndSnake(bool cleared)
    {
        _snakeOver = true;
        _snakeRunning = false;
        _snakePaused = false;
        _snakeTimer.Stop();
        UpdateSnakeHud();
        ShowSnakeOverlay(
            cleared ? "¡Tablero completo!" : "Fin de la partida",
            cleared ? $"Puntos: {_snakeScore}" : $"Chocaste. Puntos: {_snakeScore}",
            "Jugar otra vez");
    }

    private void ShowSnakeOverlay(string title, string text, string button)
    {
        SnakeOverlayTitle.Text = title;
        SnakeOverlayText.Text = text;
        SnakeOverlayButton.Content = button;
        SnakeOverlay.Visibility = Visibility.Visible;
    }

    private void ApplySnakeSpeed()
    {
        var baseMs = SnakeDifficulty.SelectedIndex switch
        {
            0 => 180,
            2 => 80,
            _ => 120,
        };
        var faster = Math.Min(_snakeScore * 4, baseMs - 50);
        _snakeTimer.Interval = TimeSpan.FromMilliseconds(baseMs - faster);
    }

    private void UpdateSnakeHud()
    {
        SnakeScoreText.Text = $"Puntos: {_snakeScore}";
        SnakeBestText.Text = $"Récord: {_snakeBest}";
    }
}
