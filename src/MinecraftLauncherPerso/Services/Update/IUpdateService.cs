namespace MinecraftLauncherPerso.Services.Update;

public sealed record UpdateInfo(Version Version, string DownloadUrl, string? ChecksumUrl);

public interface IUpdateService
{
    /// <summary>
    /// Compare la version installée à la dernière release GitHub publique du dépôt. Retourne null
    /// si aucune mise à jour n'est disponible, si le dépôt n'a pas encore de release, ou si la
    /// vérification échoue (GitHub indisponible, pas de réseau...) — jamais bloquant pour lancer
    /// le jeu.
    /// </summary>
    Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Télécharge l'exécutable de la release et remplace le launcher courant : comme le process
    /// en cours verrouille son propre .exe sur Windows, un script détaché attend sa fermeture,
    /// remplace le fichier puis relance le launcher. Termine le process courant (Environment.Exit)
    /// une fois le script lancé — n'y revient jamais.
    /// </summary>
    Task ApplyUpdateAndRestartAsync(UpdateInfo update, IProgress<string>? progress = null, CancellationToken cancellationToken = default);
}
