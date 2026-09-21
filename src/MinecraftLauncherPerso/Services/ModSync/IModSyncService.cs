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

    /// <summary>
    /// "Réparation rapide" (Paramètres) : force un retéléchargement complet du modpack en ignorant
    /// le cache ETag/Last-Modified, même si celui-ci indique "déjà à jour" — utile quand un fichier
    /// local a été corrompu ou supprimé par erreur sans que le contenu distant ait changé (auquel
    /// cas SyncAsync seul ne détecterait rien à faire).
    /// </summary>
    Task RepairAsync(
        string modpackZipUrl,
        string gameDirectory,
        IProgress<string>? progress = null,
        IProgress<double>? downloadProgress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Télécharge par avance, en arrière-plan, le modpack à jour dans un fichier temporaire partagé
    /// avec SyncAsync (voir DownloadAsync/GetPartialDownloadPath) sans l'extraire — SyncAsync,
    /// appelé plus tard au clic sur "Jouer", retrouve ce téléchargement déjà effectué (ou partiel,
    /// repris) au lieu de repartir de zéro, rendant le clic sur "Jouer" quasi instantané côté sync.
    /// Ne fait rien si le modpack est déjà à jour ou si le VPS est injoignable (best-effort, jamais
    /// d'erreur propagée à l'appelant).
    /// </summary>
    Task PrefetchAsync(
        string modpackZipUrl,
        string gameDirectory,
        IProgress<string>? progress = null,
        IProgress<double>? downloadProgress = null,
        CancellationToken cancellationToken = default);
}
