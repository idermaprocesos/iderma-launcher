using Iderma.Core.Services;

namespace Iderma_Launcher;

internal static class UpdateCheckPreference
{
    private static string FilePath => Path.Combine(UserStore.DataDirectory, "update-check.txt");

    public static bool Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return true;
            }

            return !File.ReadAllText(FilePath).Trim().Equals("off", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    public static void Save(bool enabled)
    {
        try
        {
            Directory.CreateDirectory(UserStore.DataDirectory);
            File.WriteAllText(FilePath, enabled ? "on" : "off");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    public static bool IsEnabledForCurrentUser()
    {
        var user = Session.Current;
        if (user is null || !user.Role.Equals("Desarrollador", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return Load();
    }
}
