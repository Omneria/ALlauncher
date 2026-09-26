using CmlLib.Core;

namespace MinecraftLauncherPerso.Services.Forge;

public interface IForgeManager
{
    /// <summary>
    /// Garantit qu'un profil Forge (ex. Minecraft 1.20.1 + Forge 47.4.23) est installé dans le
    /// dossier de jeu de <paramref name="launcher"/>, en l'installant si nécessaire.
    /// </summary>
    /// <returns>Identifiant de version à passer à <c>MinecraftLauncher.BuildProcessAsync</c> (ex. "1.20.1-forge-47.4.23").</returns>
    /// <param name="downloadProgress">
    /// Fraction 0.0-1.0 du téléchargement en cours (CmlLib expose déjà cette information via
    /// ByteProgress, jusqu'ici seulement transformée en texte pour <paramref name="progress"/>) :
    /// permet à l'appelant d'afficher une vraie barre de progression plutôt qu'un indicateur
    /// indéterminé pendant l'installation de Forge.
    /// </param>
    Task<string> EnsureForgeInstalledAsync(
        MinecraftLauncher launcher,
        string minecraftVersion,
        string forgeVersion,
        IProgress<string>? progress = null,
        IProgress<double>? downloadProgress = null,
        CancellationToken cancellationToken = default);
}
