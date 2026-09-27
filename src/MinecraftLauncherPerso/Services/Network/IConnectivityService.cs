namespace MinecraftLauncherPerso.Services.Network;

public interface IConnectivityService
{
    /// <summary>
    /// Faux si Windows ne voit aucun réseau, ou si aucune des adresses <paramref name="probeUrls"/>
    /// ne répond en HTTP (n'importe quel code, même une erreur, prouve que la connexion marche).
    /// Vrai dès la première réponse. Jamais d'exception, sauf annulation demandée par l'appelant.
    /// </summary>
    Task<bool> IsOnlineAsync(IEnumerable<string> probeUrls, CancellationToken cancellationToken = default);
}
