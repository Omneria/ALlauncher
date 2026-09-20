using CmlLib.Core;
using CmlLib.Core.ProcessBuilder;
using MinecraftLauncherPerso.Services.Auth;

namespace MinecraftLauncherPerso.Services.Launch;

public interface IGameLauncher
{
    /// <summary>
    /// Construit le process Forge/Minecraft (classpath, mods, JVM args) avec la session active,
    /// la RAM configurée et le Java fourni, puis le démarre. Ne bloque pas jusqu'à la fermeture du
    /// jeu : le process continue de tourner indépendamment une fois lancé. Si <paramref
    /// name="serverIp"/> est renseigné, le jeu rejoint directement ce serveur au démarrage
    /// (équivalent des arguments --server/--port du launcher officiel).
    /// </summary>
    Task<ProcessWrapper> LaunchAsync(
        MinecraftLauncher launcher,
        string versionId,
        MinecraftSession session,
        string javaExecutablePath,
        int minRamMb,
        int maxRamMb,
        string? serverIp = null,
        int serverPort = 25565,
        IProgress<string>? gameOutput = null,
        CancellationToken cancellationToken = default);
}
