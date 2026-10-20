using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Iderma_Launcher;

internal sealed class LoginSlideshow : IDisposable
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif"
    };

    private readonly Image _slideA;
    private readonly Image _slideB;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(8) };
    private readonly DispatcherTimer _reloadDebounce = new() { Interval = TimeSpan.FromMilliseconds(800) };
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly List<string> _slides = [];
    private int _index;
    private int _loadVersion;
    private int _discoverVersion;
    private bool _showingA = true;
    private bool _disposed;
    private bool _discovering;
    private bool _watchersAttached;

    public LoginSlideshow(Image slideA, Image slideB)
    {
        _slideA = slideA;
        _slideB = slideB;
        _timer.Tick += Timer_Tick;
        _reloadDebounce.Tick += ReloadDebounce_Tick;
    }

    public void Start()
    {
        if (_disposed)
        {
            return;
        }

        if (_slides.Count == 0 && !_discovering)
        {
            _discovering = true;
            var version = ++_discoverVersion;
            _ = LoadSlidesAsync(version, showImmediately: true);
        }
        else if (_watchersAttached)
        {
            foreach (var watcher in _watchers)
            {
                watcher.EnableRaisingEvents = true;
            }
        }

        if (_slides.Count > 1)
        {
            _timer.Start();
        }
    }

    public void Stop()
    {
        _timer.Stop();
        _reloadDebounce.Stop();
        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
        _timer.Tick -= Timer_Tick;
        _reloadDebounce.Tick -= ReloadDebounce_Tick;
        foreach (var watcher in _watchers)
        {
            watcher.Dispose();
        }
        _watchers.Clear();
    }

    private void Timer_Tick(object? sender, object e)
    {
        if (_slides.Count == 0)
        {
            return;
        }

        _index = (_index + 1) % _slides.Count;
        CrossfadeTo(_slides[_index]);
    }

    private void ReloadDebounce_Tick(object? sender, object e)
    {
        _reloadDebounce.Stop();
        var version = ++_discoverVersion;
        _ = LoadSlidesAsync(version, showImmediately: _slides.Count == 0);
    }

    private async Task LoadSlidesAsync(int version, bool showImmediately)
    {
        (List<string> Files, List<string> Folders) found;
        try
        {
            found = await Task.Run(DiscoverSlides).ConfigureAwait(false);
        }
        catch (Exception)
        {
            found = ([], []);
        }

        if (version != _discoverVersion || _disposed)
        {
            return;
        }

        _slideA.DispatcherQueue.TryEnqueue(() =>
        {
            if (version != _discoverVersion || _disposed)
            {
                return;
            }

            _discovering = false;
            ApplyDiscovered(found.Files, found.Folders, showImmediately);
        });
    }

    private void ApplyDiscovered(List<string> files, List<string> folders, bool showImmediately)
    {
        if (!_watchersAttached)
        {
            AttachWatchers(folders);
            _watchersAttached = true;
        }

        _slides.Clear();
        _slides.AddRange(files);

        if (_index >= _slides.Count)
        {
            _index = 0;
        }

        if (_slides.Count > 1)
        {
            _timer.Start();
        }
        else
        {
            _timer.Stop();
        }

        if (showImmediately && _slides.Count > 0)
        {
            ShowImmediate(_slides[_index]);
        }
    }

    private static (List<string> Files, List<string> Folders) DiscoverSlides()
    {
        var folders = DiscoverFolders()
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var files = folders
            .SelectMany(folder => Directory.EnumerateFiles(folder))
            .Where(path => ImageExtensions.Contains(Path.GetExtension(path)))
            .GroupBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .OrderBy(path => NaturalSortKey(Path.GetFileName(path)), StringComparer.OrdinalIgnoreCase)
            .ToList();
        return (files, folders);
    }

    private void ShowImmediate(string path)
    {
        var version = ++_loadVersion;
        try
        {
            var source = CreateSource(path);
            if (version != _loadVersion || _disposed || source is null)
            {
                return;
            }

            _slideA.Source = source;
            _slideA.Opacity = 1;
            _slideB.Opacity = 0;
            _showingA = true;
        }
        catch (Exception)
        {
            // Ignore a file that is still being copied.
        }
    }

    private void CrossfadeTo(string path)
    {
        var version = ++_loadVersion;
        try
        {
            var source = CreateSource(path);
            if (version != _loadVersion || _disposed || source is null)
            {
                return;
            }

            var incoming = _showingA ? _slideB : _slideA;
            var outgoing = _showingA ? _slideA : _slideB;
            incoming.Source = source;

            var fadeIn = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = new Duration(TimeSpan.FromSeconds(1.4)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };
            var fadeOut = new DoubleAnimation
            {
                From = 1,
                To = 0,
                Duration = new Duration(TimeSpan.FromSeconds(1.4)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };

            Storyboard.SetTarget(fadeIn, incoming);
            Storyboard.SetTargetProperty(fadeIn, "Opacity");
            Storyboard.SetTarget(fadeOut, outgoing);
            Storyboard.SetTargetProperty(fadeOut, "Opacity");

            var storyboard = new Storyboard();
            storyboard.Children.Add(fadeIn);
            storyboard.Children.Add(fadeOut);
            storyboard.Begin();

            _showingA = !_showingA;
        }
        catch (Exception)
        {
            // Ignore a file that is still being copied.
        }
    }

    private ImageSource? CreateSource(string path)
    {
        var (viewWidth, _) = GetViewPixels();
        var decodeWidth = (int)Math.Clamp(Math.Round(viewWidth), 1, 1600);
        var bitmap = new BitmapImage
        {
            DecodePixelWidth = decodeWidth
        };
        bitmap.UriSource = new Uri(Path.GetFullPath(path));
        return bitmap;
    }

    private (double Width, double Height) GetViewPixels()
    {
        var scale = _slideA.XamlRoot?.RasterizationScale ?? 1;
        if (scale < 1)
        {
            scale = 1;
        }

        var width = _slideA.ActualWidth;
        var height = _slideA.ActualHeight;
        if (width < 2 || height < 2)
        {
            var size = _slideA.XamlRoot?.Size ?? new Windows.Foundation.Size(1920, 1080);
            width = size.Width;
            height = size.Height;
        }

        return (Math.Max(1, width * scale), Math.Max(1, height * scale));
    }

    private void AttachWatchers(IEnumerable<string> folders)
    {
        foreach (var folder in folders)
        {
            try
            {
                var watcher = new FileSystemWatcher(folder)
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.Size,
                    IncludeSubdirectories = false,
                    EnableRaisingEvents = true
                };
                watcher.Created += OnFolderChanged;
                watcher.Deleted += OnFolderChanged;
                watcher.Renamed += OnFolderChanged;
                watcher.Changed += OnFolderChanged;
                _watchers.Add(watcher);
            }
            catch (Exception)
            {
                // If a folder cannot be watched, the copied images still work.
            }
        }
    }

    private void OnFolderChanged(object sender, FileSystemEventArgs e)
    {
        _slideA.DispatcherQueue.TryEnqueue(() =>
        {
            _reloadDebounce.Stop();
            _reloadDebounce.Start();
        });
    }

    private static IEnumerable<string> DiscoverFolders()
    {
        yield return Path.Combine(AppContext.BaseDirectory, "img");

        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var level = 0; level < 6 && current is not null; level++)
        {
            yield return Path.Combine(current.FullName, "img");
            current = current.Parent;
        }
    }

    private static string NaturalSortKey(string fileName)
    {
        return Regex.Replace(
            fileName,
            @"\d+",
            match => match.Value.PadLeft(8, '0'),
            RegexOptions.CultureInvariant);
    }
}
