using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Iderma.Core.Catalog;

namespace Iderma.Core.Services;

public sealed record ReleasePrograma(
    Version Version,
    string Etiqueta,
    string UrlNotas,
    string NombreArchivo,
    string UrlDescarga,
    long TamanoBytes);

public sealed record ComponenteActualizable(
    string Id,
    string Nombre,
    string Repo,
    Version VersionLocal,
    ReleasePrograma Remoto);

/// <summary>
/// Actualizaciones del launcher y de cada app según los repos de GitHub.
/// Solo se consultan el launcher y las aplicaciones habilitadas del usuario.
/// </summary>
public static class ActualizacionProgramaService
{
    public const string IdLauncher = "launcher";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly HttpClient Http = CrearCliente();

    public static Version VersionInstalada =>
        Version.TryParse(AppInfo.Version, out var version)
            ? Normalizar(version)
            : new Version(2, 0, 0, 0);

    public static string TextoVersionInstalada
    {
        get
        {
            var version = VersionInstalada;
            return $"V{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";
        }
    }

    /// <summary>
    /// Consulta el launcher y las apps cuyos ids están habilitados para el usuario.
    /// </summary>
    public static async Task<IReadOnlyList<ComponenteActualizable>> ConsultarParaUsuarioAsync(
        IEnumerable<string> appIdsHabilitados,
        Func<string, Version?> versionLocalApp,
        CancellationToken cancelar = default)
    {
        var pendientes = new List<ComponenteActualizable>();
        var habilitadas = new HashSet<string>(appIdsHabilitados, StringComparer.OrdinalIgnoreCase);

        var launcher = await ConsultarRepoAsync(
            IdLauncher,
            "Launcher",
            AppRepos.Launcher,
            VersionInstalada,
            cancelar);
        if (launcher is not null)
        {
            pendientes.Add(launcher);
        }

        foreach (var app in AppCatalog.All)
        {
            cancelar.ThrowIfCancellationRequested();
            if (!habilitadas.Contains(app.Id) || string.IsNullOrWhiteSpace(app.GitHubRepo))
            {
                continue;
            }

            var local = versionLocalApp(app.Id) ?? new Version(0, 0, 0, 0);
            var item = await ConsultarRepoAsync(app.Id, app.Name, app.GitHubRepo, local, cancelar);
            if (item is not null)
            {
                pendientes.Add(item);
            }
        }

        return pendientes;
    }

    /// <summary>Compatibilidad: consulta el instalador completo histórico.</summary>
    public static async Task<ReleasePrograma> ConsultarAsync(CancellationToken cancelar = default)
    {
        var release = await LeerReleaseAsync(AppRepos.BundleCompleto, cancelar)
            ?? throw new InvalidOperationException(
                "No hay versiones publicadas todavía en GitHub Releases.");
        return Mapear(release, AppRepos.BundleCompleto)
            ?? throw new InvalidOperationException(
                $"La versión {release.TagName} no incluye un instalador (.exe) en GitHub Releases.");
    }

    public static bool HayActualizacion(ReleasePrograma release) =>
        Normalizar(release.Version) > VersionInstalada;

    public static async Task<string> DescargarPaqueteAsync(
        ReleasePrograma release,
        IProgress<(double Porcentaje, string Mensaje)>? progreso,
        CancellationToken cancelar = default)
    {
        var carpeta = Path.Combine(Path.GetTempPath(), "Iderma-update");
        Directory.CreateDirectory(carpeta);
        var destino = Path.Combine(carpeta, SanitizarNombre(release.NombreArchivo));

        progreso?.Report((5, $"Descargando {release.NombreArchivo}…"));
        using var solicitud = CrearSolicitudDescarga(release.UrlDescarga);
        using var respuesta = await Http.SendAsync(solicitud, HttpCompletionOption.ResponseHeadersRead, cancelar);
        respuesta.EnsureSuccessStatusCode();

        var total = respuesta.Content.Headers.ContentLength ?? release.TamanoBytes;
        await using var origen = await respuesta.Content.ReadAsStreamAsync(cancelar);
        await using var archivo = File.Create(destino);
        var buffer = new byte[81_920];
        long leidos = 0;
        int n;
        while ((n = await origen.ReadAsync(buffer, cancelar)) > 0)
        {
            await archivo.WriteAsync(buffer.AsMemory(0, n), cancelar);
            leidos += n;
            if (total > 0)
            {
                var pct = Math.Clamp(10 + 80.0 * leidos / total, 10, 90);
                progreso?.Report((pct, $"Descargando… {leidos / 1_048_576d:0.0} / {total / 1_048_576d:0.0} MB"));
            }
        }

        progreso?.Report((92, "Descarga lista."));
        return destino;
    }

    public static Task<string> DescargarInstaladorAsync(
        ReleasePrograma release,
        IProgress<(double Porcentaje, string Mensaje)>? progreso,
        CancellationToken cancelar = default) =>
        DescargarPaqueteAsync(release, progreso, cancelar);

    public static void AplicarPaquete(string rutaPaquete, string carpetaDestino)
    {
        if (string.IsNullOrWhiteSpace(rutaPaquete) || !File.Exists(rutaPaquete))
        {
            throw new InvalidOperationException("No se encontró el paquete descargado.");
        }

        Directory.CreateDirectory(carpetaDestino);
        if (rutaPaquete.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            var temporal = Path.Combine(Path.GetTempPath(), "Iderma-update-extract-" + Guid.NewGuid().ToString("N"));
            try
            {
                if (Directory.Exists(temporal))
                {
                    Directory.Delete(temporal, true);
                }

                ZipFile.ExtractToDirectory(rutaPaquete, temporal);
                var origen = ResolverRaizExtraida(temporal);
                CopiarArbol(origen, carpetaDestino);
            }
            finally
            {
                try
                {
                    if (Directory.Exists(temporal))
                    {
                        Directory.Delete(temporal, true);
                    }
                }
                catch
                {
                }
            }

            return;
        }

        if (rutaPaquete.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = rutaPaquete,
                UseShellExecute = true
            });
            return;
        }

        throw new InvalidOperationException("El paquete no es un .zip ni un instalador .exe.");
    }

    /// <summary>
    /// Prepara la actualización del launcher en una carpeta temporal y lanza un script
    /// que copia los archivos cuando este proceso ya cerró (los .exe/.dll en uso no se pueden sobrescribir).
    /// </summary>
    public static void AplicarLauncherTrasCerrar(
        string rutaPaquete,
        string carpetaDestino,
        string exeLauncher,
        int processId)
    {
        if (string.IsNullOrWhiteSpace(rutaPaquete) || !File.Exists(rutaPaquete))
        {
            throw new InvalidOperationException("No se encontró el paquete descargado del launcher.");
        }

        if (!rutaPaquete.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("La actualización del launcher debe ser un .zip.");
        }

        var baseUpdate = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Iderma",
            "launcher-update");
        var staging = Path.Combine(baseUpdate, "payload");
        var script = Path.Combine(baseUpdate, "aplicar.cmd");

        if (Directory.Exists(baseUpdate))
        {
            try
            {
                Directory.Delete(baseUpdate, true);
            }
            catch
            {
                // Si queda algo bloqueado, se sobrescribe más abajo.
            }
        }

        Directory.CreateDirectory(staging);

        var temporal = Path.Combine(Path.GetTempPath(), "Iderma-launcher-extract-" + Guid.NewGuid().ToString("N"));
        try
        {
            ZipFile.ExtractToDirectory(rutaPaquete, temporal);
            CopiarArbol(ResolverRaizExtraida(temporal), staging);
        }
        finally
        {
            try
            {
                if (Directory.Exists(temporal))
                {
                    Directory.Delete(temporal, true);
                }
            }
            catch
            {
            }
        }

        var destino = carpetaDestino.TrimEnd('\\', '/');
        var origen = staging.TrimEnd('\\', '/');
        var exe = exeLauncher;
        // cmd con rutas entre comillas; espera al PID y luego robocopy + reinicio.
        var contenido =
            $"""
             @echo off
             setlocal
             set "DEST={destino}"
             set "SRC={origen}"
             set "EXE={exe}"
             set "PID={processId}"
             :wait
             tasklist /FI "PID eq %PID%" 2>nul | find "%PID%" >nul
             if not errorlevel 1 (
               timeout /t 1 /nobreak >nul
               goto wait
             )
             timeout /t 1 /nobreak >nul
             robocopy "%SRC%" "%DEST%" /E /IS /IT /R:2 /W:1 /NFL /NDL /NJH /NJS /nc /ns /np >nul
             start "" "%EXE%"
             rmdir /S /Q "%SRC%" 2>nul
             del "%~f0" 2>nul
             """;

        File.WriteAllText(script, contenido);

        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"{script}\"",
            WorkingDirectory = baseUpdate,
            UseShellExecute = false,
            CreateNoWindow = true
        });
    }

    private static string ResolverRaizExtraida(string temporal)
    {
        var entradas = Directory.GetFileSystemEntries(temporal);
        if (entradas.Length == 1 && Directory.Exists(entradas[0]))
        {
            return entradas[0];
        }

        return temporal;
    }

    public static void GuardarInstaladorPendiente(string ruta, ReleasePrograma release)
    {
        var pendiente = new InstaladorPendiente
        {
            Ruta = ruta,
            Etiqueta = release.Etiqueta,
            Version = release.Version.ToString()
        };
        Directory.CreateDirectory(Path.GetDirectoryName(RutaInstaladorPendiente())!);
        File.WriteAllText(RutaInstaladorPendiente(), JsonSerializer.Serialize(pendiente, JsonOptions));
    }

    public static InstaladorPendiente? LeerInstaladorPendiente()
    {
        var meta = RutaInstaladorPendiente();
        if (!File.Exists(meta))
        {
            return null;
        }

        try
        {
            var pendiente = JsonSerializer.Deserialize<InstaladorPendiente>(File.ReadAllText(meta), JsonOptions);
            if (pendiente is null || string.IsNullOrWhiteSpace(pendiente.Ruta) || !File.Exists(pendiente.Ruta))
            {
                File.Delete(meta);
                return null;
            }

            if (!Version.TryParse(pendiente.Version, out var version) || Normalizar(version) <= VersionInstalada)
            {
                return null;
            }

            return pendiente;
        }
        catch
        {
            try
            {
                File.Delete(meta);
            }
            catch
            {
            }

            return null;
        }
    }

    public static void LanzarInstalador(string rutaInstalador)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = rutaInstalador,
            UseShellExecute = true
        });
    }

    public static string Tamano(ReleasePrograma release) =>
        release.TamanoBytes > 0 ? $" ({release.TamanoBytes / 1_048_576d:0.0} MB)" : string.Empty;

    public static string Resumen(IReadOnlyList<ComponenteActualizable> pendientes)
    {
        if (pendientes.Count == 0)
        {
            return "Todo al día.";
        }

        return string.Join(
            ", ",
            pendientes.Select(p => $"{p.Nombre} {p.Remoto.Etiqueta}"));
    }

    public static Task<ComponenteActualizable?> ConsultarComponenteAsync(
        string id,
        string nombre,
        string repo,
        Version versionLocal,
        CancellationToken cancelar = default) =>
        ConsultarRepoAsync(id, nombre, repo, versionLocal, cancelar);

    private static async Task<ComponenteActualizable?> ConsultarRepoAsync(
        string id,
        string nombre,
        string repo,
        Version versionLocal,
        CancellationToken cancelar)
    {
        try
        {
            var release = await LeerReleaseAsync(repo, cancelar);
            if (release is null)
            {
                return null;
            }

            var mapeado = Mapear(release, repo);
            if (mapeado is null)
            {
                return null;
            }

            if (Normalizar(mapeado.Version) <= Normalizar(versionLocal))
            {
                return null;
            }

            return new ComponenteActualizable(id, nombre, repo, Normalizar(versionLocal), mapeado);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidOperationException ex) when (
            ex.Message.Contains("privados", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("token", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("limitó", StringComparison.OrdinalIgnoreCase))
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<ReleaseGithub?> LeerReleaseAsync(string repo, CancellationToken cancelar)
    {
        var json = await DescargarTextoAsync(AppRepos.ApiLatest(repo), cancelar);
        ReleaseGithub? release = null;
        if (!string.IsNullOrWhiteSpace(json))
        {
            release = JsonSerializer.Deserialize<ReleaseGithub>(json, JsonOptions);
        }

        if (release is not null && !string.IsNullOrWhiteSpace(release.TagName) && !release.Draft)
        {
            return release;
        }

        json = await DescargarTextoAsync(
            $"https://api.github.com/repos/{repo}/releases?per_page=10",
            cancelar);
        var lista = string.IsNullOrWhiteSpace(json)
            ? []
            : JsonSerializer.Deserialize<List<ReleaseGithub>>(json, JsonOptions) ?? [];
        return lista.FirstOrDefault(r => !r.Draft && !r.Prerelease)
               ?? lista.FirstOrDefault(r => !r.Draft);
    }

    private static async Task<string?> DescargarTextoAsync(string url, CancellationToken cancelar)
    {
        using var solicitud = new HttpRequestMessage(HttpMethod.Get, url);
        AdjuntarAuth(solicitud);
        using var respuesta = await Http.SendAsync(solicitud, cancelar);
        if (respuesta.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        if ((int)respuesta.StatusCode is 401 or 403)
        {
            if (GitHubCredencial.Obtener() is null)
            {
                throw new InvalidOperationException(
                    "No se pudo acceder a las actualizaciones. Pida a TI que configure el acceso a GitHub en este equipo.");
            }

            throw new InvalidOperationException(
                "GitHub rechazó el acceso o limitó las consultas. Espere un minuto e inténtelo de nuevo.");
        }

        if ((int)respuesta.StatusCode == 429)
        {
            throw new InvalidOperationException(
                "GitHub limitó las consultas. Espere un minuto e inténtelo de nuevo.");
        }

        respuesta.EnsureSuccessStatusCode();
        return await respuesta.Content.ReadAsStringAsync(cancelar);
    }

    private static HttpRequestMessage CrearSolicitudDescarga(string url)
    {
        var solicitud = new HttpRequestMessage(HttpMethod.Get, url);
        AdjuntarAuth(solicitud);
        // Assets privados: la URL de la API exige este Accept.
        if (url.Contains("api.github.com", StringComparison.OrdinalIgnoreCase)
            && url.Contains("/releases/assets/", StringComparison.OrdinalIgnoreCase))
        {
            solicitud.Headers.Accept.Clear();
            solicitud.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
        }

        return solicitud;
    }

    private static void AdjuntarAuth(HttpRequestMessage solicitud)
    {
        var token = GitHubCredencial.Obtener();
        if (!string.IsNullOrWhiteSpace(token))
        {
            solicitud.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }

    private static ReleasePrograma? Mapear(ReleaseGithub release, string repo)
    {
        Version version;
        try
        {
            version = ParsearVersion(release.TagName);
        }
        catch
        {
            return null;
        }

        var asset = ElegirPaquete(release.Assets);
        if (asset is null)
        {
            return null;
        }

        // Repos privados: preferir URL de API (requiere token + Accept octet-stream).
        var urlDescarga = !string.IsNullOrWhiteSpace(asset.ApiUrl)
            ? asset.ApiUrl!
            : asset.BrowserDownloadUrl;
        if (string.IsNullOrWhiteSpace(urlDescarga))
        {
            return null;
        }

        return new ReleasePrograma(
            version,
            release.TagName,
            release.HtmlUrl ?? $"https://github.com/{repo}/releases",
            asset.Name ?? "paquete.zip",
            urlDescarga,
            asset.Size);
    }

    private static AssetGithub? ElegirPaquete(IReadOnlyList<AssetGithub>? assets)
    {
        if (assets is null || assets.Count == 0)
        {
            return null;
        }

        bool EsZip(AssetGithub asset) =>
            Nombre(asset).EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

        bool EsExe(AssetGithub asset) =>
            Nombre(asset).EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

        bool EsRedist(AssetGithub asset)
        {
            var nombre = Nombre(asset);
            return nombre.Contains("redist", StringComparison.OrdinalIgnoreCase)
                   || nombre.Contains("webview", StringComparison.OrdinalIgnoreCase);
        }

        return assets.FirstOrDefault(EsZip)
               ?? assets.FirstOrDefault(a => EsExe(a) && !EsRedist(a)
                                             && (Nombre(a).Contains("Launcher", StringComparison.OrdinalIgnoreCase)
                                                 || Nombre(a).Contains("Iderma-V", StringComparison.OrdinalIgnoreCase)
                                                 || Nombre(a).Contains("Setup", StringComparison.OrdinalIgnoreCase)
                                                 || Nombre(a).Contains("IdermaCapilarApp-V", StringComparison.OrdinalIgnoreCase)
                                                 || Nombre(a).Contains("instalador", StringComparison.OrdinalIgnoreCase)))
               ?? assets.FirstOrDefault(a => EsExe(a) && !EsRedist(a));
    }

    private static string Nombre(AssetGithub asset) => asset.Name ?? string.Empty;

    private static void CopiarArbol(string origen, string destino)
    {
        foreach (var dir in Directory.GetDirectories(origen, "*", SearchOption.AllDirectories))
        {
            var relativo = Path.GetRelativePath(origen, dir);
            Directory.CreateDirectory(Path.Combine(destino, relativo));
        }

        foreach (var archivo in Directory.GetFiles(origen, "*", SearchOption.AllDirectories))
        {
            var relativo = Path.GetRelativePath(origen, archivo);
            var meta = Path.Combine(destino, relativo);
            Directory.CreateDirectory(Path.GetDirectoryName(meta)!);
            File.Copy(archivo, meta, overwrite: true);
        }
    }

    private static Version ParsearVersion(string etiqueta)
    {
        var limpio = etiqueta.Trim();
        if (limpio.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            limpio = limpio[1..].Trim().TrimStart('.').Trim();
        }

        var coincidencia = Regex.Match(limpio, @"\d+(?:\.\d+){1,3}");
        if (!coincidencia.Success || !Version.TryParse(coincidencia.Value, out var version))
        {
            throw new InvalidOperationException($"No se reconoció la versión «{etiqueta}».");
        }

        return Normalizar(version);
    }

    private static Version Normalizar(Version version) =>
        new(version.Major, version.Minor, Math.Max(version.Build, 0), Math.Max(version.Revision, 0));

    private static string SanitizarNombre(string nombre)
    {
        var invalido = Path.GetInvalidFileNameChars();
        var limpio = new string(nombre.Where(c => !invalido.Contains(c)).ToArray());
        return string.IsNullOrWhiteSpace(limpio) ? "Iderma-update.bin" : limpio;
    }

    private static string RutaInstaladorPendiente() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Iderma",
            "instalador-programa.json");

    private static HttpClient CrearCliente()
    {
        var cliente = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(15)
        };
        cliente.DefaultRequestHeaders.UserAgent.ParseAdd("IdermaLauncher/15.0.0");
        cliente.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        cliente.DefaultRequestHeaders.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        return cliente;
    }

    public sealed class InstaladorPendiente
    {
        public string Ruta { get; set; } = string.Empty;
        public string Etiqueta { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
    }

    private sealed class ReleaseGithub
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = string.Empty;

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }

        [JsonPropertyName("draft")]
        public bool Draft { get; set; }

        [JsonPropertyName("prerelease")]
        public bool Prerelease { get; set; }

        [JsonPropertyName("assets")]
        public List<AssetGithub> Assets { get; set; } = [];
    }

    private sealed class AssetGithub
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("url")]
        public string? ApiUrl { get; set; }

        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; set; }

        [JsonPropertyName("size")]
        public long Size { get; set; }
    }
}
