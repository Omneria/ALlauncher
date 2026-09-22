namespace MinecraftLauncherPerso.Services.ModSync;

public interface IModSyncService
{
    /// <summary>
    /// Synchronise le pack de mods/config. Deux modes, selon <paramref name="manifestUrl"/> :
    /// - Non vide : mode incrémental, un manifest JSON (chemin -> URL + sha256) est comparé
    ///   fichier par fichier au contenu local ; seuls les fichiers dont le hash a changé sont
    ///   retéléchargés.
    /// - Vide/null : ancien mode, une archive .zip unique hébergée sur le VPS
    ///   (<paramref name="modpackZipUrl"/>) est retéléchargée en bloc uniquement si elle a changé
    ///   côté serveur (ETag/Last-Modified), pas à chaque lancement.
    /// </summary>
    /// <param name="downloadProgress">
    /// Fraction 0.0-1.0 du téléchargement en cours (null si le serveur ne fournit pas de
    /// Content-Length, ou si aucun téléchargement n'a lieu car déjà à jour) : permet à l'appelant
    /// d'afficher une vraie barre de progression plutôt qu'un indicateur indéterminé.
    /// </param>
    Task SyncAsync(
        string modpackZipUrl,
        string? manifestUrl,
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
    /// "Réparation rapide" (Paramètres) : en mode zip, force un retéléchargement complet en
    /// ignorant le cache ETag/Last-Modified, même si celui-ci indique "déjà à jour" — utile quand
    /// un fichier local a été corrompu/supprimé sans que le contenu distant ait changé. En mode
    /// manifest, équivaut à SyncAsync (chaque fichier y est déjà revérifié par hash à chaque appel,
    /// pas de cache à invalider).
    /// </summary>
    Task RepairAsync(
        string modpackZipUrl,
        string? manifestUrl,
        string gameDirectory,
        IProgress<string>? progress = null,
        IProgress<double>? downloadProgress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Télécharge par avance, en arrière-plan, le modpack à jour dans un fichier temporaire partagé
    /// avec SyncAsync (voir DownloadAsync/GetPartialDownloadPath) sans l'extraire — SyncAsync,
    /// appelé plus tard au clic sur "Jouer", retrouve ce téléchargement déjà effectué (ou partiel,
    /// repris) au lieu de repartir de zéro, rendant le clic sur "Jouer" quasi instantané côté sync.
    /// Ne fait rien si le modpack est déjà à jour, si le VPS est injoignable (best-effort, jamais
    /// d'erreur propagée à l'appelant), ou si le mode manifest est actif (chaque fichier y est déjà
    /// petit et téléchargé individuellement à la demande, moins besoin de précharger en bloc).
    /// </summary>
    Task PrefetchAsync(
        string modpackZipUrl,
        string? manifestUrl,
        string gameDirectory,
        IProgress<string>? progress = null,
        IProgress<double>? downloadProgress = null,
        CancellationToken cancellationToken = default);

    /// <summary>Vrai si une version précédente du modpack (mods/config) a été sauvegardée par un
    /// SyncAsync/RepairAsync antérieur et peut être restaurée via RollbackAsync.</summary>
    bool HasRollbackAvailable(string gameDirectory);

    /// <summary>
    /// Restaure la version de mods/config sauvegardée juste avant la dernière mise à jour
    /// appliquée (un seul cran de recul, pas un historique complet) — utile si une mise à jour du
    /// modpack casse quelque chose en attendant un correctif côté VPS. Effet temporaire : tant que
    /// le VPS sert toujours le contenu problématique, une synchronisation normale ultérieure peut
    /// le retélécharger et annuler ce retour en arrière (voir le commentaire dans l'implémentation).
    /// </summary>
    Task RollbackAsync(
        string gameDirectory,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}
