namespace Iderma.Core.Catalog;

/// <summary>
/// Repositorios GitHub del ecosistema. El desarrollo local puede seguir en el monorepo;
/// cada app publica releases desde su propio repositorio.
/// </summary>
public static class AppRepos
{
    public const string Organizacion = "idermaprocesos";

    public const string Core = Organizacion + "/iderma-core";
    public const string Launcher = Organizacion + "/iderma-launcher";
    public const string BundleCompleto = Organizacion + "/idermalogistica";

    public static string Url(string repo) => $"https://github.com/{repo}";

    public static string ApiLatest(string repo) =>
        $"https://api.github.com/repos/{repo}/releases/latest";
}
