using System.Runtime.InteropServices;
using System.Text;

namespace Iderma_Launcher;

internal static class TaskbarIcon
{
    private static readonly Guid AppUserModelId = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");
    private const uint RelaunchCommand = 2;
    private const uint RelaunchIconResource = 3;
    private const uint RelaunchDisplayName = 4;
    private const uint AppModelId = 5;
    private const ushort VtLpWstr = 31;
    private const int ShcneUpdateItem = 0x00002000;
    private const uint ShcnfPath = 0x0005;
    private const uint SlgpRawPath = 0x00000004;

    public static void Apply(IntPtr hwnd, string iconPath)
    {
        if (hwnd == IntPtr.Zero || !File.Exists(iconPath))
        {
            return;
        }

        try
        {
            SetRelaunchIcon(hwnd, iconPath);
        }
        catch (Exception ex) when (ex is COMException or InvalidComObjectException or IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }

    private static void SetRelaunchIcon(IntPtr hwnd, string iconPath)
    {
        var iid = typeof(IPropertyStore).GUID;
        var hr = SHGetPropertyStoreForWindow(hwnd, ref iid, out var store);
        if (hr < 0 || store is null)
        {
            return;
        }

        try
        {
            SetString(store, AppModelId, TaskbarJumpList.ModelId);
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(exe))
            {
                SetString(store, RelaunchCommand, exe);
            }

            SetString(store, RelaunchDisplayName, "Iderma");
            SetString(store, RelaunchIconResource, iconPath + ",0");
            store.Commit();
        }
        finally
        {
            Marshal.ReleaseComObject(store);
        }
    }

    public static void RepairPinned(string iconPath)
    {
        try
        {
            RepairPinnedShortcuts(iconPath);
        }
        catch (Exception ex) when (ex is COMException or InvalidComObjectException or IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }

    private static void RepairPinnedShortcuts(string iconPath)
    {
        var pinned = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft",
            "Internet Explorer",
            "Quick Launch",
            "User Pinned",
            "TaskBar");
        if (!Directory.Exists(pinned))
        {
            return;
        }

        var exePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exePath))
        {
            return;
        }

        foreach (var shortcut in Directory.EnumerateFiles(pinned, "*.lnk"))
        {
            if (!TryPointShortcutAt(shortcut, exePath, iconPath))
            {
                continue;
            }

            SHChangeNotify(ShcneUpdateItem, ShcnfPath, shortcut, IntPtr.Zero);
        }
    }

    private static bool TryPointShortcutAt(string shortcutPath, string exePath, string iconPath)
    {
        var link = (IShellLinkW)new ShellLink();
        var file = (IPersistFile)link;
        file.Load(shortcutPath, 2);

        var target = new StringBuilder(1024);
        link.GetPath(target, target.Capacity, IntPtr.Zero, SlgpRawPath);
        if (!string.Equals(target.ToString(), exePath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var currentIcon = new StringBuilder(1024);
        link.GetIconLocation(currentIcon, currentIcon.Capacity, out var index);
        var iconOk = index == 0 && string.Equals(currentIcon.ToString(), iconPath, StringComparison.OrdinalIgnoreCase);
        var idChanged = EnsureModelId(link);
        if (iconOk && !idChanged)
        {
            return false;
        }

        if (!iconOk)
        {
            link.SetIconLocation(iconPath, 0);
        }

        file.Save(shortcutPath, true);
        return true;
    }

    private static bool EnsureModelId(IShellLinkW link)
    {
        var store = (IPropertyStore)link;
        var key = new PropertyKey { FormatId = AppUserModelId, PropertyId = AppModelId };
        store.GetValue(ref key, out var current);
        try
        {
            if (current.VariantType == VtLpWstr && current.Pointer != IntPtr.Zero)
            {
                var text = Marshal.PtrToStringUni(current.Pointer);
                if (string.Equals(text, TaskbarJumpList.ModelId, StringComparison.Ordinal))
                {
                    return false;
                }
            }
        }
        finally
        {
            PropVariantClear(ref current);
        }

        SetString(store, AppModelId, TaskbarJumpList.ModelId);
        store.Commit();
        return true;
    }

    private static void SetString(IPropertyStore store, uint propertyId, string text)
    {
        var key = new PropertyKey { FormatId = AppUserModelId, PropertyId = propertyId };
        var value = new PropVariantString
        {
            VariantType = VtLpWstr,
            Pointer = Marshal.StringToCoTaskMemUni(text)
        };
        try
        {
            store.SetValue(ref key, ref value);
        }
        finally
        {
            PropVariantClear(ref value);
        }
    }

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
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink
    {
    }

    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out ushort pwHotkey);
        void SetHotkey(ushort wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport]
    [Guid("0000010b-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPersistFile
    {
        void GetClassID(out Guid classId);
        [PreserveSig]
        int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string fileName, uint mode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string fileName, bool remember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string fileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string fileName);
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetPropertyStoreForWindow(
        IntPtr hwnd,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IPropertyStore? propertyStore);

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariantString value);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(int eventId, uint flags, string? item1, IntPtr item2);
}
