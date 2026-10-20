using Iderma.Core.Catalog;
using Iderma.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Iderma_Launcher;

public sealed class AppCard
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string LogoPath { get; init; }
    public required string StatusLabel { get; init; }
    public required AppStatus Status { get; init; }
    public required Brush StatusBackground { get; init; }
    public required Brush StatusForeground { get; init; }
    public bool CanLaunch { get; init; }
    public Visibility LockVisibility { get; init; }
    public double CardOpacity { get; init; } = 1;

    public static AppCard From(AppDefinition app)
    {
        var (background, foreground) = ColorsFor(app.Status);
        return new AppCard
        {
            Id = app.Id,
            Name = app.Name,
            Description = app.Description,
            LogoPath = AppCatalog.GetLogoUri(app.LogoFileName),
            StatusLabel = AppCatalog.StatusLabel(app.Status),
            Status = app.Status,
            StatusBackground = new SolidColorBrush(background),
            StatusForeground = new SolidColorBrush(foreground),
            CanLaunch = !app.IsLocked,
            LockVisibility = app.IsLocked ? Visibility.Visible : Visibility.Collapsed,
            CardOpacity = app.IsLocked ? 0.55 : 1
        };
    }

    private static (Color Background, Color Foreground) ColorsFor(AppStatus status) => status switch
    {
        AppStatus.Alpha => (Color.FromArgb(64, 180, 83, 9), Color.FromArgb(255, 224, 164, 96)),
        AppStatus.Beta => (Color.FromArgb(64, 0, 120, 212), Color.FromArgb(255, 126, 186, 236)),
        AppStatus.InImplementation => (Color.FromArgb(64, 16, 124, 16), Color.FromArgb(255, 132, 198, 132)),
        AppStatus.Private => (Color.FromArgb(64, 196, 43, 28), Color.FromArgb(255, 236, 154, 144)),
        _ => (Color.FromArgb(64, 160, 160, 160), Color.FromArgb(255, 196, 196, 196))
    };
}
