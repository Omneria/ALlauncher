namespace MinecraftLauncherPerso.Services.ModSync;

public interface IModSyncService
{
    /// <summary>
    /// Synchronise le pack de mods/config depuis une archive .zip unique hébergée sur le VPS
    /// (ex. http://vps/modpack/Algaron-modded.zip). Ne la retélécharge que si elle a changé
    /// côté serveur depuis la dernière synchro, pas à chaque lancement.
    /// </summary>
    /// <param name="downloadProgress">
    /// Fraction 0.0-1.0 du téléchargement en cours (null si le serveur ne fournit pas de
    /// Content-Length, ou si aucun téléchargement n'a lieu car déjà à jour) : permet à l'appelant
    /// d'afficher une vraie barre de progression plutôt qu'un indicateur indéterminé.
    /// </param>
    Task SyncAsync(
        string modpackZipUrl,
        string gameDirectory,
        IProgress<string>? progress = null,
        IProgress<double>? downloadProgress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Horodatage de la dernière synchro confirmée (téléchargement réel ou simple vérification
    /// "déjà à jour"), lu depuis le cache local sans requête réseau. Null si jamais synchronisé.
    /// </summary>
    DateTimeOffset? GetLastSyncedAt(string gameDirectory);
}
