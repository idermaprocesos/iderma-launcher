using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Iderma_Launcher;

internal static class UserPhoto
{
    public static async Task<BitmapImage?> LoadAsync(string? path, int? decodePixelWidth = null)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var image = new BitmapImage();
            if (decodePixelWidth is > 0)
            {
                image.DecodePixelWidth = decodePixelWidth.Value;
            }

            var file = await StorageFile.GetFileFromPathAsync(path);
            using var source = await file.OpenAsync(FileAccessMode.Read);
            var memory = new InMemoryRandomAccessStream();
            await RandomAccessStream.CopyAsync(source, memory);
            memory.Seek(0);
            await image.SetSourceAsync(memory);
            return image;
        }
        catch
        {
            return null;
        }
    }

    public static async Task ShowAsync(XamlRoot? root, string? path, string title, string? emptyMessage = null)
    {
        if (root is null)
        {
            return;
        }

        var image = await LoadAsync(path);
        if (image is null)
        {
            var empty = new ContentDialog
            {
                XamlRoot = root,
                Title = string.IsNullOrWhiteSpace(title) ? "Foto" : title,
                Content = string.IsNullOrWhiteSpace(emptyMessage)
                    ? "Esta cuenta todavía no tiene foto."
                    : emptyMessage,
                CloseButtonText = "Cerrar"
            };
            await empty.ShowAsync();
            return;
        }

        var pixelWidth = image.PixelWidth > 0 ? image.PixelWidth : 900;
        var pixelHeight = image.PixelHeight > 0 ? image.PixelHeight : 1200;
        var bounds = root.Size;
        var maxImageWidth = Math.Max(360, bounds.Width - 160);
        var maxImageHeight = Math.Max(280, bounds.Height - 240);
        var scale = Math.Min(maxImageWidth / pixelWidth, maxImageHeight / pixelHeight);
        var imageWidth = Math.Max(280, Math.Round(pixelWidth * scale));
        var imageHeight = Math.Max(280, Math.Round(pixelHeight * scale));

        var closed = new TaskCompletionSource();
        var host = new Grid
        {
            Width = bounds.Width,
            Height = bounds.Height,
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x99, 0, 0, 0)),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        var card = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(20, 16, 20, 16),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Background = BrushOr("SolidBackgroundFillColorBaseBrush", Windows.UI.Color.FromArgb(255, 32, 32, 32)),
            BorderBrush = BrushOr("CardStrokeColorDefaultBrush", Windows.UI.Color.FromArgb(255, 80, 80, 80)),
            Child = new StackPanel
            {
                Spacing = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                Children =
                {
                    new TextBlock
                    {
                        Text = string.IsNullOrWhiteSpace(title) ? "Foto" : title,
                        FontSize = 20,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        HorizontalAlignment = HorizontalAlignment.Center
                    },
                    CreateViewer(image, imageWidth, imageHeight),
                    new Button
                    {
                        Content = "Cerrar",
                        MinWidth = 120,
                        HorizontalAlignment = HorizontalAlignment.Center
                    }
                }
            }
        };

        var popup = new Popup
        {
            XamlRoot = root,
            Child = host,
            HorizontalOffset = 0,
            VerticalOffset = 0,
            IsLightDismissEnabled = false
        };

        void OnRootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
        {
            host.Width = sender.Size.Width;
            host.Height = sender.Size.Height;
        }

        void Close()
        {
            root.Changed -= OnRootChanged;
            popup.IsOpen = false;
            closed.TrySetResult();
        }

        if (card.Child is StackPanel panel && panel.Children.OfType<Button>().FirstOrDefault() is Button closeButton)
        {
            closeButton.Click += (_, _) => Close();
        }

        host.Children.Add(card);
        host.PointerPressed += (_, args) =>
        {
            if (ReferenceEquals(args.OriginalSource, host))
            {
                Close();
            }
        };

        root.Changed += OnRootChanged;
        popup.IsOpen = true;
        await closed.Task;
    }

    private static ScrollViewer CreateViewer(BitmapImage image, double width, double height)
    {
        var viewer = new ScrollViewer
        {
            Width = width,
            Height = height,
            HorizontalAlignment = HorizontalAlignment.Center,
            ZoomMode = ZoomMode.Enabled,
            MinZoomFactor = 1,
            MaxZoomFactor = 4,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Enabled,
            VerticalScrollMode = ScrollMode.Enabled,
            Content = new Image
            {
                Source = image,
                Width = width,
                Height = height,
                Stretch = Stretch.Uniform,
                IsHitTestVisible = false
            }
        };

        Windows.Foundation.Point? grab = null;
        double grabX = 0;
        double grabY = 0;

        viewer.PointerPressed += (_, args) =>
        {
            if (!args.GetCurrentPoint(viewer).Properties.IsLeftButtonPressed)
            {
                return;
            }

            grab = args.GetCurrentPoint(viewer).Position;
            grabX = viewer.HorizontalOffset;
            grabY = viewer.VerticalOffset;
            viewer.CapturePointer(args.Pointer);
            args.Handled = true;
        };

        viewer.PointerMoved += (_, args) =>
        {
            if (grab is null || !args.GetCurrentPoint(viewer).Properties.IsLeftButtonPressed)
            {
                return;
            }

            var point = args.GetCurrentPoint(viewer).Position;
            viewer.ChangeView(grabX - (point.X - grab.Value.X), grabY - (point.Y - grab.Value.Y), null, true);
            args.Handled = true;
        };

        viewer.PointerReleased += (_, args) =>
        {
            grab = null;
            viewer.ReleasePointerCapture(args.Pointer);
        };
        viewer.PointerCaptureLost += (_, _) => grab = null;
        viewer.PointerWheelChanged += (_, args) =>
        {
            var delta = args.GetCurrentPoint(viewer).Properties.MouseWheelDelta;
            if (delta == 0)
            {
                return;
            }

            var current = viewer.ZoomFactor;
            var next = Math.Clamp(current * (delta > 0 ? 1.12 : 1 / 1.12), viewer.MinZoomFactor, viewer.MaxZoomFactor);
            var point = args.GetCurrentPoint(viewer).Position;
            var originX = (viewer.HorizontalOffset + point.X) / current;
            var originY = (viewer.VerticalOffset + point.Y) / current;
            viewer.ChangeView(originX * next - point.X, originY * next - point.Y, (float)next, true);
            args.Handled = true;
        };
        return viewer;
    }

    private static Brush BrushOr(string key, Windows.UI.Color fallback)
    {
        if (Application.Current.Resources.TryGetValue(key, out var value) && value is Brush brush)
        {
            return brush;
        }

        return new SolidColorBrush(fallback);
    }
}
