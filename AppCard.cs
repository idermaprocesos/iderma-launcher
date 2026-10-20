using Iderma.Core.Catalog;
using Iderma.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Iderma_Launcher;

public enum AppCardState
{
    Ready,
    Missing,
    UpdateRequired
}

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
    public AppCardState State { get; init; } = AppCardState.Ready;
    public bool RequiresDownload => State is AppCardState.Missing or AppCardState.UpdateRequired;

    public static AppCard From(AppDefinition app, AppCardState state = AppCardState.Ready)
    {
        var locked = app.IsLocked;
        var needsAction = !locked && state != AppCardState.Ready;
        var (background, foreground) = ColorsFor(app.Status);
        var label = state switch
        {
            AppCardState.Missing => "Instalar",
            AppCardState.UpdateRequired => "Actualizar",
            _ => AppCatalog.StatusLabel(app.Status)
        };
        if (needsAction)
        {
            background = state == AppCardState.Missing
                ? Color.FromArgb(64, 120, 120, 120)
                : Color.FromArgb(64, 180, 83, 9);
            foreground = state == AppCardState.Missing
                ? Color.FromArgb(255, 200, 200, 200)
                : Color.FromArgb(255, 224, 164, 96);
        }

        return new AppCard
        {
            Id = app.Id,
            Name = app.Name,
            Description = app.Description,
            LogoPath = AppCatalog.GetCardLogoUri(app.Id, deshabilitado: needsAction),
            StatusLabel = label,
            Status = app.Status,
            StatusBackground = new SolidColorBrush(background),
            StatusForeground = new SolidColorBrush(foreground),
            CanLaunch = !locked,
            LockVisibility = locked ? Visibility.Visible : Visibility.Collapsed,
            CardOpacity = locked ? 0.55 : 1,
            State = locked ? AppCardState.Ready : state
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
