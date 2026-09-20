using CmlLib.Core;
using CmlLib.Core.ProcessBuilder;
using MinecraftLauncherPerso.Models;
using MinecraftLauncherPerso.Services.Auth;

namespace MinecraftLauncherPerso.Services.Launch;

public interface IGameLauncher
{
    /// <summary>
    /// Déclenché quand une partie lancée par <see cref="LaunchAsync"/> se termine, avec son code
    /// de sortie (0 = fermeture normale). Permet à l'UI de proposer d'ouvrir les logs en cas de
    /// crash (code non nul) sans avoir à sonder le process soi-même.
    /// </summary>
    event EventHandler<int>? GameExited;

    /// <summary>
    /// Construit le process Forge/Minecraft (classpath, mods, JVM args) avec la session active,
    /// la RAM/serveur/résolution configurés (<paramref name="settings"/>) et le Java fourni, puis
    /// le démarre. Ne bloque pas jusqu'à la fermeture du jeu : le process continue de tourner
    /// indépendamment une fois lancé. Si <see cref="LauncherSettings.ServerHost"/> est renseigné,
    /// le jeu rejoint directement ce serveur au démarrage (équivalent des arguments
    /// --server/--port du launcher officiel).
    /// </summary>
    Task<ProcessWrapper> LaunchAsync(
        MinecraftLauncher launcher,
        string versionId,
        MinecraftSession session,
        string javaExecutablePath,
        LauncherSettings settings,
        IProgress<string>? gameOutput = null,
        CancellationToken cancellationToken = default);
}
