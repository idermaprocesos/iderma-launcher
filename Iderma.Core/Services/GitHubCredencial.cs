namespace Iderma.Core.Services;

/// <summary>
/// Token para la API de GitHub (repos privados). Solo fuentes silenciosas
/// (variables de entorno o archivo local). No invoca git ni abre logins.
/// </summary>
internal static class GitHubCredencial
{
    private static readonly object Candado = new();
    private static string? _cache;
    private static bool _intentado;

    public static string? Obtener()
    {
        lock (Candado)
        {
            if (_intentado)
            {
                return _cache;
            }

            _intentado = true;
            _cache = LeerEntorno() ?? LeerArchivoLocal();
            return _cache;
        }
    }

    public static void InvalidarCache()
    {
        lock (Candado)
        {
            _intentado = false;
            _cache = null;
        }
    }

    private static string? LeerEntorno()
    {
        foreach (var nombre in new[] { "GH_TOKEN", "GITHUB_TOKEN", "IDERMA_GITHUB_TOKEN" })
        {
            var valor = Environment.GetEnvironmentVariable(nombre);
            if (!string.IsNullOrWhiteSpace(valor))
            {
                return valor.Trim();
            }
        }

        return null;
    }

    private static string? LeerArchivoLocal()
    {
        var ruta = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Iderma",
            "github.token");
        if (!File.Exists(ruta))
        {
            return null;
        }

        try
        {
            var texto = File.ReadAllText(ruta).Trim();
            return string.IsNullOrWhiteSpace(texto) ? null : texto;
        }
        catch
        {
            return null;
        }
    }
}
