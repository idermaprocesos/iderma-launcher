using System.Diagnostics;
using System.Runtime.InteropServices;
using Iderma.Core.Catalog;

namespace Iderma_Launcher;

/// <summary>
/// Abre cada área desde la carpeta <c>areas</c> del repositorio.
/// Cuentas sigue en el proyecto Iderma.UserAdmin, junto al launcher.
/// Base de datos autoriza acciones sensibles del resto de apps.
/// </summary>
internal static class AreaAppLauncher
{
    internal const string AlreadyRunning = "already-running";

    private static readonly object Gate = new();
    private static readonly Dictionary<string, DateTime> RecentLaunches = new(StringComparer.OrdinalIgnoreCase);
    private static readonly string? RepoRoot = FindRepoRoot();

    private static readonly Dictionary<string, AppTarget> Targets =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [AppCatalog.Logistica] = Area("ficha2", "IdermaCapilarApp.exe", "IdermaFichas.csproj"),
            [AppCatalog.Clinica] = Area("CLINICA", "IdermaClinica.exe", "IdermaClinica.csproj"),
            [AppCatalog.Incidencias] = Area("incidencias", "IdermaIncidencias.exe", "IdermaIncidencias.csproj"),
            [AppCatalog.Historial] = Area("HISTORIAL", "IdermaCapilar.Historial.exe", "IdermaCapilar.Historial.csproj"),
            [AppCatalog.Clientes] = Area("clientes", "IdermaClinica.exe", "IdermaClinica.csproj"),
            [AppCatalog.Mantenimiento] = Area("mantenimiento", "MantenimientoCapilar.exe", "MantenimientoCapilar.csproj"),
            [AppCatalog.Hardware] = Area("hardware", "HardwareRegistro.exe", "HardwareRegistro.csproj"),
            [AppCatalog.Ti] = Area("ti", "MesaAyudaTI.exe", "MesaAyudaTI.csproj"),
            [AppCatalog.Administracion] = Area("administracion", "IdermaAdministracion.exe", "IdermaAdministracion.csproj"),
            [AppCatalog.Gerencia] = Area("gerencia", "IdermaGerencia.exe", "IdermaGerencia.csproj"),
            [AppCatalog.Marketing] = Area("record", "BitacoraGrabacion.exe", "BitacoraGrabacion.csproj"),
            [AppCatalog.Procesos] = Area(@"pros\Iderma.Procesos", "Iderma.Procesos.exe", "Iderma.Procesos.csproj"),
            [AppCatalog.Reclamos] = Area("reclamos", "LibroReclamaciones.exe", "LibroReclamaciones.csproj"),
            [AppCatalog.BaseDeDatos] = Area("basededatos", "IdermaBaseDatos.exe", "IdermaBaseDatos.csproj"),
            [AppCatalog.Desarrollador] = Area("consola", "Consola.exe", "Consola.csproj"),
            [AppCatalog.Cuentas] = new(
                RepoRoot is null ? string.Empty : Path.Combine(RepoRoot, "Iderma.UserAdmin"),
                "Iderma.UserAdmin.exe",
                "Iderma.UserAdmin.csproj")
        };

    public static bool TryGetTarget(string appId, out AppTarget target) =>
        Targets.TryGetValue(appId, out target!);

    public static string AppsInstallRoot =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Iderma",
            "Apps");

    public static string InstallFolder(string appId) =>
        Path.Combine(AppsInstallRoot, appId.Trim().ToLowerInvariant());

    /// <summary>Solo cuenta la instalación en %LOCALAPPDATA%\Iderma\Apps (no bin\Debug).</summary>
    public static bool IsInstalled(string appId) => FindInstalledExe(appId) is not null;

    public static string? FindExe(string appId) =>
        Targets.TryGetValue(appId, out var target) ? FindDebugExe(target, appId) : null;

    public static string? FindInstalledExe(string appId)
    {
        if (!Targets.TryGetValue(appId, out var target) || string.IsNullOrWhiteSpace(target.ExeFileName))
        {
            return null;
        }

        return BuscarExeEnCarpeta(InstallFolder(appId), target.ExeFileName);
    }

    public static Version? VersionInstalada(string appId)
    {
        // Solo la carpeta instalada: sin ella la app aparece como «Instalar».
        var exe = FindInstalledExe(appId);
        if (exe is null || !File.Exists(exe))
        {
            return null;
        }

        try
        {
            var info = FileVersionInfo.GetVersionInfo(exe);
            if (Version.TryParse(info.FileVersion, out var version))
            {
                return new Version(
                    version.Major,
                    version.Minor,
                    Math.Max(version.Build, 0),
                    Math.Max(version.Revision, 0));
            }

            if (Version.TryParse(info.ProductVersion?.Split('+')[0], out version))
            {
                return new Version(
                    version.Major,
                    version.Minor,
                    Math.Max(version.Build, 0),
                    Math.Max(version.Revision, 0));
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException or ArgumentException)
        {
        }

        return null;
    }

    public static bool TryGetCommand(string appId, out AppLaunchCommand command)
    {
        command = default;
        if (!Targets.TryGetValue(appId, out var target) || RepoRoot is null || !Directory.Exists(target.Folder))
        {
            return false;
        }

        var exe = FindDebugExe(target);
        if (exe is not null)
        {
            command = new AppLaunchCommand(exe, string.Empty, Path.GetDirectoryName(exe)!, exe);
            return true;
        }

        if (string.IsNullOrWhiteSpace(target.ProjectFileName))
        {
            return false;
        }

        var project = Path.Combine(target.Folder, target.ProjectFileName);
        var dotnet = FindDotNet();
        if (!File.Exists(project) || dotnet is null)
        {
            return false;
        }

        var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "taskbar.ico");
        command = new AppLaunchCommand(
            dotnet,
            $"run --project \"{project}\"",
            target.Folder,
            File.Exists(icon) ? icon : dotnet);
        return true;
    }

    private static string? FindDotNet()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "dotnet", "dotnet.exe")
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public static bool IsRunning(string appId)
    {
        if (!Targets.TryGetValue(appId, out var target))
        {
            return false;
        }

        var processes = FindRunning(target);
        var running = processes.Count > 0;
        foreach (var process in processes)
        {
            process.Dispose();
        }

        return running;
    }

    public static void ForceClose(string appId)
    {
        lock (Gate)
        {
            RecentLaunches.Remove(appId);
        }

        if (!Targets.TryGetValue(appId, out var target))
        {
            return;
        }

        foreach (var process in FindRunning(target))
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
            {
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    public static void RestartLauncher()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = exe,
            WorkingDirectory = Path.GetDirectoryName(exe) ?? AppContext.BaseDirectory,
            UseShellExecute = true
        });

        App.MainAppWindow?.CerrarParaActualizar();
    }

    public static string Launch(string appId)
    {
        if (IsDoubleClick(appId))
        {
            ScheduleBringToFront(appId);
            return string.Empty;
        }

        if (IsRunning(appId))
        {
            MarkClick(appId);
            return AlreadyRunning;
        }

        if (!Targets.TryGetValue(appId, out var target))
        {
            return "Esta aplicación aún no tiene un programa asociado.";
        }

        if (RepoRoot is null)
        {
            return "No se encontró la carpeta areas junto al launcher.";
        }

        if (!Directory.Exists(target.Folder))
        {
            return $"No se encontró la carpeta:\n{target.Folder}";
        }

        var exe = FindDebugExe(target);
        if (exe is not null)
        {
            StartTracked(appId, () => StartProcess(exe, Path.GetDirectoryName(exe)!));
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(target.ProjectFileName))
        {
            return $"No hay un ejecutable compilado en:\n{target.Folder}";
        }

        var project = Path.Combine(target.Folder, target.ProjectFileName);
        if (!File.Exists(project))
        {
            return $"No hay un ejecutable compilado ni el proyecto:\n{project}";
        }

        StartTracked(appId, () => StartProcess("dotnet", target.Folder, $"run --project \"{project}\""));
        return string.Empty;
    }

    private static void ScheduleBringToFront(string appId)
    {
        _ = BringToFrontLaterAsync(appId);
    }

    private static async Task BringToFrontLaterAsync(string appId)
    {
        await Task.Delay(150).ConfigureAwait(false);
        var until = DateTime.UtcNow.AddMilliseconds(1200);
        while (DateTime.UtcNow < until)
        {
            if (TryBringToFront(appId))
            {
                await Task.Delay(200).ConfigureAwait(false);
                TryBringToFront(appId);
                return;
            }

            await Task.Delay(100).ConfigureAwait(false);
        }
    }

    private static bool TryBringToFront(string appId)
    {
        if (!Targets.TryGetValue(appId, out var target))
        {
            return false;
        }

        var processes = FindRunning(target);
        try
        {
            foreach (var process in processes)
            {
                process.Refresh();
                var hwnd = process.MainWindowHandle;
                if (hwnd == IntPtr.Zero)
                {
                    hwnd = FindMainWindow(process.Id);
                }

                if (hwnd == IntPtr.Zero)
                {
                    continue;
                }

                FocusWindow(hwnd);
                return true;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }

        return false;
    }

    private static void FocusWindow(IntPtr hwnd)
    {
        var foreground = GetForegroundWindow();
        var foregroundThread = GetWindowThreadProcessId(foreground, out _);
        var currentThread = GetCurrentThreadId();
        var attached = foregroundThread != 0
            && foregroundThread != currentThread
            && AttachThreadInput(foregroundThread, currentThread, true);

        ShowWindow(hwnd, SwRestore);
        SetWindowPos(hwnd, HwndTop, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpShowWindow);
        SetForegroundWindow(hwnd);

        if (attached)
        {
            AttachThreadInput(foregroundThread, currentThread, false);
        }
    }

    private static IntPtr FindMainWindow(int processId)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid != (uint)processId || !IsWindowVisible(hwnd) || GetWindow(hwnd, GwOwner) != IntPtr.Zero)
            {
                return true;
            }

            found = hwnd;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    private const int SwRestore = 9;
    private const uint GwOwner = 4;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpShowWindow = 0x0040;
    private static readonly IntPtr HwndTop = IntPtr.Zero;

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    private static bool IsDoubleClick(string appId)
    {
        lock (Gate)
        {
            return RecentLaunches.TryGetValue(appId, out var startedAt)
                && DateTime.UtcNow - startedAt < DoubleClickWindow();
        }
    }

    private static void MarkClick(string appId)
    {
        lock (Gate)
        {
            RecentLaunches[appId] = DateTime.UtcNow;
        }
    }

    private static TimeSpan DoubleClickWindow()
    {
        var milliseconds = GetDoubleClickTime();
        if (milliseconds == 0)
        {
            milliseconds = 500;
        }

        return TimeSpan.FromMilliseconds(milliseconds);
    }

    [DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();

    private static void StartTracked(string appId, Action start)
    {
        MarkClick(appId);

        try
        {
            start();
        }
        catch
        {
            lock (Gate)
            {
                RecentLaunches.Remove(appId);
            }

            throw;
        }
    }

    private static List<Process> FindRunning(AppTarget target)
    {
        var found = new List<Process>();
        if (string.IsNullOrWhiteSpace(target.ExeFileName) || string.IsNullOrWhiteSpace(target.Folder))
        {
            return found;
        }

        var name = Path.GetFileNameWithoutExtension(target.ExeFileName);
        var expected = FindDebugExe(target, appId: null);
        var folder = target.Folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var installFolder = string.Empty;
        foreach (var pair in Targets)
        {
            if (ReferenceEquals(pair.Value, target) || pair.Value.ExeFileName == target.ExeFileName
                && string.Equals(pair.Value.Folder, target.Folder, StringComparison.OrdinalIgnoreCase))
            {
                installFolder = InstallFolder(pair.Key) + Path.DirectorySeparatorChar;
                break;
            }
        }

        foreach (var process in Process.GetProcessesByName(name))
        {
            if (Matches(process, expected, folder, installFolder))
            {
                found.Add(process);
            }
            else
            {
                process.Dispose();
            }
        }

        return found;
    }

    private static bool Matches(Process process, string? expected, string folder, string installFolder)
    {
        try
        {
            if (process.HasExited)
            {
                return false;
            }

            var path = process.MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            var full = Path.GetFullPath(path);
            if (expected is not null
                && string.Equals(full, Path.GetFullPath(expected), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!string.IsNullOrEmpty(folder) && full.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return !string.IsNullOrEmpty(installFolder)
                   && full.StartsWith(installFolder, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return false;
        }
    }

    private static AppTarget Area(string folder, string exeFileName, string projectFileName) =>
        new(
            RepoRoot is null ? string.Empty : Path.Combine(RepoRoot, "areas", folder),
            exeFileName,
            projectFileName);

    private static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "areas"))
                && Directory.Exists(Path.Combine(dir.FullName, "Iderma.Launcher")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }

    private static string? FindDebugExe(AppTarget target, string? appId = null)
    {
        if (string.IsNullOrWhiteSpace(target.ExeFileName))
        {
            return null;
        }

        // Preferir la copia instalada (actualizaciones) antes que bin\Debug del monorepo.
        if (!string.IsNullOrWhiteSpace(appId))
        {
            var instalada = BuscarExeEnCarpeta(InstallFolder(appId), target.ExeFileName);
            if (instalada is not null)
            {
                return instalada;
            }
        }

        var bin = Path.Combine(target.Folder, "bin");
        if (Directory.Exists(bin))
        {
            var debug = Directory.EnumerateFiles(bin, target.ExeFileName, SearchOption.AllDirectories)
                .Where(path => path.Contains($"{Path.DirectorySeparatorChar}Debug{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (debug is not null)
            {
                return debug;
            }
        }

        var directo = Path.Combine(target.Folder, target.ExeFileName);
        if (File.Exists(directo))
        {
            return directo;
        }

        if (!Directory.Exists(target.Folder))
        {
            return null;
        }

        return Directory.EnumerateFiles(target.Folder, target.ExeFileName, SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static string? BuscarExeEnCarpeta(string carpeta, string exeFileName)
    {
        if (!Directory.Exists(carpeta))
        {
            return null;
        }

        var directo = Path.Combine(carpeta, exeFileName);
        if (File.Exists(directo))
        {
            return directo;
        }

        return Directory.EnumerateFiles(carpeta, exeFileName, SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static void StartProcess(string fileName, string workingDirectory, string? arguments = null)
    {
        var info = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = true
        };
        if (!string.IsNullOrWhiteSpace(arguments))
        {
            info.Arguments = arguments;
        }

        Process.Start(info);
    }

    public static void CloseOpenedApps()
    {
        foreach (var target in Targets.Values)
        {
            var processes = FindRunning(target);
            foreach (var process in processes)
            {
                try
                {
                    if (process.HasExited)
                    {
                        continue;
                    }

                    process.CloseMainWindow();
                    if (!process.WaitForExit(400))
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
                {
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
    }
}

internal sealed record AppTarget(
    string Folder,
    string? ExeFileName,
    string? ProjectFileName);

internal readonly record struct AppLaunchCommand(
    string FileName,
    string Arguments,
    string WorkingDirectory,
    string IconPath);
