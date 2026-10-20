using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Iderma_Launcher;

public sealed partial class ArcadeWindow
{
    private static readonly (int Rows, int Cols, int Mines, int Cell)[] MineLevels =
    [
        (9, 9, 10, 34),
        (16, 16, 40, 28),
        (16, 30, 99, 24),
    ];

    private DispatcherTimer _mineTimer = null!;
    private MineCell[,]? _cells;
    private int _rows;
    private int _cols;
    private int _mineTotal;
    private int _seconds;
    private int _safeRevealed;
    private bool _mineStarted;
    private bool _mineOver;

    private SolidColorBrush _hiddenBrush = null!;
    private SolidColorBrush _openBrush = null!;
    private SolidColorBrush _mineBrush = null!;
    private SolidColorBrush _hitBrush = null!;
    private SolidColorBrush _flagBrush = null!;
    private SolidColorBrush _badFlagBrush = null!;
    private SolidColorBrush _whiteBrush = null!;
    private SolidColorBrush[] _numberBrushes = null!;

    private void InitMineBrushes()
    {
        _hiddenBrush = BrushOf(0xFF3C4049);
        _openBrush = BrushOf(0xFFE6E8EE);
        _mineBrush = BrushOf(0xFF8D3A3A);
        _hitBrush = BrushOf(0xFFD32F2F);
        _flagBrush = BrushOf(0xFFFFB74D);
        _badFlagBrush = BrushOf(0xFFE53935);
        _whiteBrush = BrushOf(0xFFFFFFFF);
        _numberBrushes =
        [
            _whiteBrush,
            BrushOf(0xFF1976D2),
            BrushOf(0xFF388E3C),
            BrushOf(0xFFD32F2F),
            BrushOf(0xFF7B1FA2),
            BrushOf(0xFFB71C1C),
            BrushOf(0xFF00838F),
            BrushOf(0xFF424242),
            BrushOf(0xFF616161),
        ];
    }

    private void MineFace_Click(object sender, RoutedEventArgs e) => StartMines();

    private void MineDifficulty_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready)
        {
            return;
        }

        StartMines();
    }

    private void StartMines()
    {
        _mineTimer.Stop();
        var level = MineLevels[Math.Clamp(MineDifficulty.SelectedIndex, 0, MineLevels.Length - 1)];
        _rows = level.Rows;
        _cols = level.Cols;
        _mineTotal = level.Mines;
        _seconds = 0;
        _safeRevealed = 0;
        _mineStarted = false;
        _mineOver = false;
        _cells = new MineCell[_rows, _cols];

        MineBoard.Children.Clear();
        MineBoard.RowDefinitions.Clear();
        MineBoard.ColumnDefinitions.Clear();
        MineFaceButton.Content = "🙂";
        MineStatusText.Text = "";

        for (var r = 0; r < _rows; r++)
        {
            MineBoard.RowDefinitions.Add(new RowDefinition { Height = new GridLength(level.Cell) });
        }

        for (var c = 0; c < _cols; c++)
        {
            MineBoard.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(level.Cell) });
        }

        for (var r = 0; r < _rows; r++)
        {
            for (var c = 0; c < _cols; c++)
            {
                var label = new TextBlock
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontWeight = FontWeights.Bold,
                    FontSize = level.Cell * 0.42,
                    IsHitTestVisible = false,
                };
                var box = new Border
                {
                    Background = _hiddenBrush,
                    CornerRadius = new CornerRadius(3),
                    Margin = new Thickness(1),
                    Child = label,
                };
                var row = r;
                var col = c;
                box.Tapped += (_, _) => OnMineLeft(row, col);
                box.RightTapped += (_, args) =>
                {
                    args.Handled = true;
                    ToggleFlag(row, col);
                };
                Grid.SetRow(box, r);
                Grid.SetColumn(box, c);
                MineBoard.Children.Add(box);
                _cells[r, c] = new MineCell { View = box, Label = label };
            }
        }

        UpdateMineHud();
    }

    private void OnMineTick()
    {
        if (!_mineStarted || _mineOver || _seconds >= 5999)
        {
            return;
        }

        _seconds++;
        UpdateMineHud();
    }

    private void OnMineLeft(int row, int col)
    {
        if (_mineOver || !Inside(row, col))
        {
            return;
        }

        var cell = _cells![row, col];
        if (cell.Revealed)
        {
            Chord(row, col);
            return;
        }

        Reveal(row, col);
    }

    private void Reveal(int row, int col)
    {
        if (_mineOver || !Inside(row, col))
        {
            return;
        }

        var cell = _cells![row, col];
        if (cell.Revealed || cell.Flagged)
        {
            return;
        }

        if (!_mineStarted)
        {
            PlaceMines(row, col);
            _mineStarted = true;
            _mineTimer.Start();
        }

        Flood(row, col);
        if (_mineOver)
        {
            return;
        }

        if (_safeRevealed >= _rows * _cols - _mineTotal)
        {
            WinMines();
        }
    }

    private void Flood(int row, int col)
    {
        if (_mineOver || !Inside(row, col))
        {
            return;
        }

        var cell = _cells![row, col];
        if (cell.Revealed || cell.Flagged)
        {
            return;
        }

        cell.Revealed = true;
        if (cell.Mine)
        {
            LoseMines(cell);
            return;
        }

        _safeRevealed++;
        PaintCell(cell);
        if (cell.Neighbors != 0)
        {
            return;
        }

        for (var dr = -1; dr <= 1; dr++)
        {
            for (var dc = -1; dc <= 1; dc++)
            {
                if (dr == 0 && dc == 0)
                {
                    continue;
                }

                Flood(row + dr, col + dc);
            }
        }
    }

    private void Chord(int row, int col)
    {
        var cell = _cells![row, col];
        if (!cell.Revealed || cell.Neighbors == 0 || _mineOver)
        {
            return;
        }

        var flags = 0;
        foreach (var (nr, nc) in Around(row, col))
        {
            if (_cells[nr, nc].Flagged)
            {
                flags++;
            }
        }

        if (flags != cell.Neighbors)
        {
            return;
        }

        foreach (var (nr, nc) in Around(row, col))
        {
            if (_mineOver)
            {
                return;
            }

            if (!_cells[nr, nc].Flagged)
            {
                Reveal(nr, nc);
            }
        }
    }

    private void ToggleFlag(int row, int col)
    {
        if (_mineOver || !Inside(row, col))
        {
            return;
        }

        var cell = _cells![row, col];
        if (cell.Revealed)
        {
            return;
        }

        cell.Flagged = !cell.Flagged;
        PaintCell(cell);
        UpdateMineHud();
    }

    private void PlaceMines(int safeRow, int safeCol)
    {
        var spots = OpenSpots(safeRow, safeCol, wide: true);
        if (spots.Count < _mineTotal)
        {
            spots = OpenSpots(safeRow, safeCol, wide: false);
        }

        for (var i = spots.Count - 1; i > 0; i--)
        {
            var j = Random.Shared.Next(i + 1);
            (spots[i], spots[j]) = (spots[j], spots[i]);
        }

        for (var i = 0; i < _mineTotal && i < spots.Count; i++)
        {
            var (r, c) = spots[i];
            _cells![r, c].Mine = true;
        }

        for (var r = 0; r < _rows; r++)
        {
            for (var c = 0; c < _cols; c++)
            {
                if (_cells![r, c].Mine)
                {
                    continue;
                }

                var count = 0;
                foreach (var (nr, nc) in Around(r, c))
                {
                    if (_cells[nr, nc].Mine)
                    {
                        count++;
                    }
                }

                _cells[r, c].Neighbors = count;
            }
        }
    }

    private List<(int R, int C)> OpenSpots(int safeRow, int safeCol, bool wide)
    {
        var spots = new List<(int R, int C)>();
        for (var r = 0; r < _rows; r++)
        {
            for (var c = 0; c < _cols; c++)
            {
                if (r == safeRow && c == safeCol)
                {
                    continue;
                }

                if (wide && Math.Abs(r - safeRow) <= 1 && Math.Abs(c - safeCol) <= 1)
                {
                    continue;
                }

                spots.Add((r, c));
            }
        }

        return spots;
    }

    private void LoseMines(MineCell hit)
    {
        _mineOver = true;
        _mineTimer.Stop();
        MineFaceButton.Content = "😵";
        MineStatusText.Text = "¡Había una mina!";
        foreach (var cell in EachCell())
        {
            if (cell.Mine)
            {
                if (cell.Flagged && cell != hit)
                {
                    continue;
                }

                cell.Revealed = true;
                cell.Flagged = false;
                PaintMine(cell, cell == hit);
            }
            else if (cell.Flagged)
            {
                cell.Label.Text = "✕";
                cell.Label.Foreground = _badFlagBrush;
            }
        }

        UpdateMineHud();
    }

    private void WinMines()
    {
        _mineOver = true;
        _mineTimer.Stop();
        MineFaceButton.Content = "😎";
        MineStatusText.Text = "¡Tablero despejado!";
        foreach (var cell in EachCell())
        {
            if (!cell.Mine || cell.Flagged)
            {
                continue;
            }

            cell.Flagged = true;
            PaintCell(cell);
        }

        UpdateMineHud();
    }

    private void PaintCell(MineCell cell)
    {
        if (!cell.Revealed)
        {
            cell.View.Background = _hiddenBrush;
            cell.Label.Text = cell.Flagged ? "⚑" : "";
            cell.Label.Foreground = _flagBrush;
            return;
        }

        cell.View.Background = _openBrush;
        if (cell.Neighbors == 0)
        {
            cell.Label.Text = "";
            return;
        }

        cell.Label.Text = cell.Neighbors.ToString();
        cell.Label.Foreground = _numberBrushes[Math.Clamp(cell.Neighbors, 1, 8)];
    }

    private void PaintMine(MineCell cell, bool hit)
    {
        cell.View.Background = hit ? _hitBrush : _mineBrush;
        cell.Label.Text = "●";
        cell.Label.Foreground = _whiteBrush;
    }

    private void UpdateMineHud()
    {
        var flags = 0;
        if (_cells is not null)
        {
            foreach (var cell in EachCell())
            {
                if (cell.Flagged)
                {
                    flags++;
                }
            }
        }

        MineCountText.Text = $"Minas: {_mineTotal - flags}";
        MineTimeText.Text = $"Tiempo: {_seconds / 60:00}:{_seconds % 60:00}";
    }

    private bool Inside(int row, int col) => row >= 0 && col >= 0 && row < _rows && col < _cols;

    private IEnumerable<(int R, int C)> Around(int row, int col)
    {
        for (var dr = -1; dr <= 1; dr++)
        {
            for (var dc = -1; dc <= 1; dc++)
            {
                if (dr == 0 && dc == 0 || !Inside(row + dr, col + dc))
                {
                    continue;
                }

                yield return (row + dr, col + dc);
            }
        }
    }

    private IEnumerable<MineCell> EachCell()
    {
        for (var r = 0; r < _rows; r++)
        {
            for (var c = 0; c < _cols; c++)
            {
                yield return _cells![r, c];
            }
        }
    }

    private static SolidColorBrush BrushOf(uint hex) => new(Color.FromArgb(
        (byte)(hex >> 24),
        (byte)(hex >> 16),
        (byte)(hex >> 8),
        (byte)hex));

    private sealed class MineCell
    {
        public bool Mine;
        public bool Revealed;
        public bool Flagged;
        public int Neighbors;
        public Border View = null!;
        public TextBlock Label = null!;
    }
}
