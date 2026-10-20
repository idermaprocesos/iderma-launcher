using System.Globalization;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using Iderma.Core.Catalog;
using Iderma.Core.Models;

namespace Iderma_Launcher;

internal static class TaskbarJumpList
{
    internal const string ModelId = "Iderma.Launcher";
    private const string OpenArg = "--open";
    private const string PipeName = "Iderma.Launcher.OpenApp";
    private const ushort VtLpWstr = 31;

    private static readonly Guid TitleFormatId = new("F29F85E0-4FF9-1068-AB91-08002B27B3D9");
    private static readonly Guid ObjectArrayId = new("92CA9DCD-5622-4bba-A805-5E9F541BD8C9");
    private static string? _pendingAppId;

    public static void SetProcessId()
    {
        SetCurrentProcessExplicitAppUserModelID(ModelId);
    }

    public static string? ReadOpenRequest()
    {
        var args = Environment.GetCommandLineArgs();
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], OpenArg, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1].Trim();
            }
        }

        return null;
    }

    public static bool TryForward(string appId)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(400);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine(appId);
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static void RememberPending(string appId) => _pendingAppId = appId;

    public static string? TakePending()
    {
        var pending = _pendingAppId;
        _pendingAppId = null;
        return pending;
    }

    public static void StartListening()
    {
        _ = Task.Run(ListenAsync);
    }

    public static void ShowFor(UserAccount user)
    {
        var apps = AppCatalog.All
            .Where(app => !app.IsLocked && user.AppIds.Contains(app.Id, StringComparer.OrdinalIgnoreCase))
            .OrderBy(app => app.Status == AppStatus.Private ? 1 : 0)
            .ThenBy(app => app.Id.Equals(AppCatalog.Cuentas, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenBy(app => app.Name, StringComparer.Create(new CultureInfo("es-ES"), ignoreCase: true))
            .ToList();
        Publish(apps);
    }

    public static void Clear()
    {
        try
        {
            var list = (ICustomDestinationList)new DestinationList();
            list.SetAppID(ModelId);
            list.DeleteList(ModelId);
            Marshal.ReleaseComObject(list);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }

    private static async Task ListenAsync()
    {
        while (true)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync().ConfigureAwait(false);
                using var reader = new StreamReader(server);
                var line = await reader.ReadLineAsync().ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var appId = line.Trim();
                var window = App.MainAppWindow;
                window?.DispatcherQueue.TryEnqueue(() => window.LaunchFromJumpList(appId));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException)
            {
                await Task.Delay(300).ConfigureAwait(false);
            }
        }
    }

    private static void Publish(IReadOnlyList<AppDefinition> apps)
    {
        ICustomDestinationList? list = null;
        var removed = IntPtr.Zero;
        IObjectCollection? collection = null;
        try
        {
            list = (ICustomDestinationList)new DestinationList();
            if (list.SetAppID(ModelId) < 0)
            {
                return;
            }

            var arrayId = ObjectArrayId;
            if (list.BeginList(out _, ref arrayId, out removed) < 0)
            {
                return;
            }

            if (apps.Count == 0)
            {
                list.AbortList();
                list.DeleteList(ModelId);
                return;
            }

            collection = (IObjectCollection)new EnumerableObjectCollection();
            var added = 0;
            foreach (var app in apps)
            {
                var link = CreateLink(app);
                if (link is null)
                {
                    continue;
                }

                if (collection.AddObject(link) >= 0)
                {
                    added++;
                }
            }

            if (added == 0)
            {
                list.AbortList();
                list.DeleteList(ModelId);
                return;
            }

            if (list.AppendCategory("Aplicaciones", collection) < 0)
            {
                list.AbortList();
                return;
            }

            list.CommitList();
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            try
            {
                list?.AbortList();
            }
            catch (COMException)
            {
            }

            System.Diagnostics.Debug.WriteLine(ex);
        }
        finally
        {
            if (removed != IntPtr.Zero)
            {
                Marshal.Release(removed);
            }

            if (collection is not null)
            {
                Marshal.ReleaseComObject(collection);
            }

            if (list is not null)
            {
                Marshal.ReleaseComObject(list);
            }
        }
    }

    private static object? CreateLink(AppDefinition app)
    {
        if (!AreaAppLauncher.TryGetCommand(app.Id, out var command))
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
            {
                return null;
            }

            var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "taskbar.ico");
            command = new AppLaunchCommand(
                exe,
                $"{OpenArg} {app.Id}",
                Path.GetDirectoryName(exe) ?? AppContext.BaseDirectory,
                File.Exists(icon) ? icon : exe);
        }

        if (!File.Exists(command.FileName))
        {
            return null;
        }

        var link = (IShellLinkW)new ShellLink();
        link.SetPath(command.FileName);
        link.SetArguments(command.Arguments);
        link.SetWorkingDirectory(command.WorkingDirectory);
        link.SetDescription(app.Description);
        link.SetShowCmd(1);
        if (File.Exists(command.IconPath))
        {
            link.SetIconLocation(command.IconPath, 0);
        }

        var store = (IPropertyStore)link;
        var key = new PropertyKey { FormatId = TitleFormatId, PropertyId = 2 };
        var value = new PropVariantString
        {
            VariantType = VtLpWstr,
            Pointer = Marshal.StringToCoTaskMemUni(app.Name)
        };
        try
        {
            store.SetValue(ref key, ref value);
            store.Commit();
        }
        finally
        {
            PropVariantClear(ref value);
        }

        return link;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariantString value);

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        public Guid FormatId;
        public uint PropertyId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariantString
    {
        public ushort VariantType;
        public ushort Reserved1;
        public ushort Reserved2;
        public ushort Reserved3;
        public IntPtr Pointer;
    }

    [ComImport]
    [Guid("77f10cf0-3db5-4966-b520-b7c54fd35ed6")]
    private class DestinationList
    {
    }

    [ComImport]
    [Guid("2d3468c1-36a7-43b6-ac24-d3f02fd9607a")]
    private class EnumerableObjectCollection
    {
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink
    {
    }

    [ComImport]
    [Guid("6332debf-87b5-4670-90c0-5e57b408a49e")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICustomDestinationList
    {
        [PreserveSig]
        int SetAppID([MarshalAs(UnmanagedType.LPWStr)] string appId);

        [PreserveSig]
        int BeginList(out uint minSlots, ref Guid iid, out IntPtr removed);

        [PreserveSig]
        int AppendCategory([MarshalAs(UnmanagedType.LPWStr)] string category, [MarshalAs(UnmanagedType.Interface)] object items);

        [PreserveSig]
        int AppendKnownCategory(int category);

        [PreserveSig]
        int AddUserTasks([MarshalAs(UnmanagedType.Interface)] object items);

        [PreserveSig]
        int CommitList();

        [PreserveSig]
        int GetRemovedDestinations(ref Guid iid, out IntPtr removed);

        [PreserveSig]
        int DeleteList([MarshalAs(UnmanagedType.LPWStr)] string appId);

        [PreserveSig]
        int AbortList();
    }

    [ComImport]
    [Guid("5632b1a4-e38a-400a-928a-d4cd63230295")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IObjectCollection
    {
        [PreserveSig]
        int GetCount(out uint count);

        [PreserveSig]
        int GetAt(uint index, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object item);

        [PreserveSig]
        int AddObject([MarshalAs(UnmanagedType.IUnknown)] object item);

        [PreserveSig]
        int AddFromArray([MarshalAs(UnmanagedType.Interface)] object source);

        [PreserveSig]
        int RemoveObjectAt(uint index);

        [PreserveSig]
        int Clear();
    }

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint propertyCount);
        void GetAt(uint propertyIndex, out PropertyKey key);
        void GetValue(ref PropertyKey key, out PropVariantString value);
        void SetValue(ref PropertyKey key, ref PropVariantString value);
        void Commit();
    }

    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int capacity, IntPtr findData, uint flags);
        void GetIDList(out IntPtr idList);
        void SetIDList(IntPtr idList);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int capacity);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int capacity);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int capacity);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        void GetHotkey(out ushort hotkey);
        void SetHotkey(ushort hotkey);
        void GetShowCmd(out int showCmd);
        void SetShowCmd(int showCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int capacity, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string relativePath, uint reserved);
        void Resolve(IntPtr hwnd, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }
}
