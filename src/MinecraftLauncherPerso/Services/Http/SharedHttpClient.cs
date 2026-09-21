using System.Net.Http;

namespace MinecraftLauncherPerso.Services.Http;

/// <summary>
/// Instance HttpClient unique, injectée dans les services qui en acceptent une optionnellement
/// (ModSyncService, NewsService, GitHubUpdateService, JavaManager/AdoptiumApiClient,
/// MicrosoftAuthService) : chacun instanciait sinon son propre <c>new HttpClient()</c> par défaut,
/// l'anti-pattern classique en .NET (chaque HttpClient garde son propre pool de connexions/sockets).
/// Tous ces services sont des singletons créés une fois au démarrage de MainWindow, donc ça ne
/// fuyait rien de concret ici, mais les regrouper reste la pratique recommandée et évite d'avoir à
/// y repenser si l'un de ces services était un jour recréé plus dynamiquement.
/// </summary>
public static class SharedHttpClient
{
    public static readonly HttpClient Instance = CreateInstance();

    private static HttpClient CreateInstance()
    {
        var client = new HttpClient();

        // Plusieurs des API utilisées (GitHub, Adoptium, Xbox Live...) rejettent (403) les
        // requêtes sans User-Agent, traitées comme du trafic automatisé suspect : posé une bonne
        // fois pour toutes ici plutôt que dans chaque service consommateur.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ALLauncher/1.0");

        return client;
    }
}
