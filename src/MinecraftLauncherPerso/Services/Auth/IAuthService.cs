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
}
