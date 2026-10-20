using Iderma.Core.Services;

namespace Iderma_Launcher;

internal enum AppViewMode
{
    Grid,
    List,
    Compact
}

internal static class AppViewPreference
{
    private static string FilePath => Path.Combine(UserStore.DataDirectory, "app-view.txt");

    public static AppViewMode Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return AppViewMode.Grid;
            }

            return File.ReadAllText(FilePath).Trim().ToLowerInvariant() switch
            {
                "list" => AppViewMode.List,
                "compact" => AppViewMode.Compact,
                _ => AppViewMode.Grid
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return AppViewMode.Grid;
        }
    }

    public static void Save(AppViewMode mode)
    {
        try
        {
            Directory.CreateDirectory(UserStore.DataDirectory);
            var text = mode switch
            {
                AppViewMode.List => "list",
                AppViewMode.Compact => "compact",
                _ => "grid"
            };
            File.WriteAllText(FilePath, text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
