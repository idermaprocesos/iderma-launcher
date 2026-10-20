using System.Runtime.InteropServices;
using Iderma.Core.Services;
using Microsoft.UI.Xaml;
using Windows.Networking.Connectivity;

namespace Iderma_Launcher;

internal static class RedClinica
{
    public static readonly string[] Nombres =
    [
        "Iderma Capilar 1",
        "Iderma Capilar 1 Plus"
    ];

    private static int _cerrando;

    public static bool EstaConectada()
    {
        foreach (var perfil in NetworkInformation.GetConnectionProfiles())
        {
            if (EsLaRedActiva(perfil))
            {
                return true;
            }
        }

        return EsLaRedActiva(NetworkInformation.GetInternetConnectionProfile());
    }

    private static string SsidActivo()
    {
        foreach (var perfil in NetworkInformation.GetConnectionProfiles())
        {
            if (!perfil.IsWlanConnectionProfile
                || perfil.GetNetworkConnectivityLevel() == NetworkConnectivityLevel.None)
            {
                continue;
            }

            try
            {
                var ssid = perfil.WlanConnectionProfileDetails?.GetConnectedSsid();
                if (!string.IsNullOrWhiteSpace(ssid))
                {
                    return ssid.Trim();
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or COMException)
            {
            }
        }

        return string.Empty;
    }

    public static void AvisarYCerrar(string motivo)
    {
        if (Interlocked.Exchange(ref _cerrando, 1) == 1)
        {
            return;
        }

        try
        {
            var tarea = Task.Run(() => VigilanciaAcceso.RegistrarAsync(motivo, SsidActivo()));
            tarea.Wait(TimeSpan.FromSeconds(8));
        }
        catch
        {
        }

        const string mensaje = "No se encuentra conectado con el ecosistema";
        MessageBox(IntPtr.Zero, mensaje, "Ecosistema", 0x00000010);
        Environment.Exit(0);
    }

    public static Task AvisarYCerrarAsync(XamlRoot? root) =>
        Task.Run(() => AvisarYCerrar("bypass"));

    private static bool EsLaRedActiva(ConnectionProfile? perfil)
    {
        if (perfil is null || !perfil.IsWlanConnectionProfile)
        {
            return false;
        }

        if (perfil.GetNetworkConnectivityLevel() == NetworkConnectivityLevel.None)
        {
            return false;
        }

        try
        {
            var ssid = perfil.WlanConnectionProfileDetails?.GetConnectedSsid()?.Trim();
            return Nombres.Any(nombre => string.Equals(ssid, nombre, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or COMException)
        {
            return false;
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
}
