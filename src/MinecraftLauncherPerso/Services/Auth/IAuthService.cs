namespace MinecraftLauncherPerso.Services.Auth;

public sealed record MinecraftSession(string Username, string Uuid, string AccessToken);

public interface IAuthService
{
    /// <summary>
    /// Authentifie l'utilisateur via OAuth Microsoft (navigateur système + redirection sur boucle
    /// locale) puis la chaîne Xbox Live -> XSTS -> Minecraft, et retourne la session à utiliser
    /// pour lancer le jeu. Réutilise silencieusement une session Microsoft précédemment mise en
    /// cache tant qu'elle est valide ; ne redemande une connexion interactive (navigateur) que si
    /// elle a expiré ou n'existe pas encore.
    /// </summary>
    Task<MinecraftSession> GetActiveSessionAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Variante silencieuse : tente de réutiliser une session Microsoft déjà en cache (même
    /// mécanisme que GetActiveSessionAsync) mais n'ouvre jamais de navigateur — retourne null si
    /// aucun compte n'est en cache ou si le token a expiré/été révoqué. Utilisée au démarrage du
    /// launcher pour réafficher automatiquement le profil connecté sans forcer une reconnexion
    /// interactive à chaque lancement.
    /// </summary>
    Task<MinecraftSession?> TryGetCachedSessionAsync(CancellationToken cancellationToken = default);
}
