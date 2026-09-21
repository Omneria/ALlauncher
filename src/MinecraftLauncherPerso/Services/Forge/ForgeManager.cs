using CmlLib.Core;
using CmlLib.Core.Installer.Forge;
using CmlLib.Core.Installers;

namespace MinecraftLauncherPerso.Services.Forge;

/// <summary>
/// Installe Forge via CmlLib.Core.Installer.Forge : <see cref="ForgeInstaller"/> ne fait
/// qu'installer/mapper le profil de version Forge composé (vanilla + Forge) — il faut ensuite
/// appeler <c>MinecraftLauncher.InstallAsync</c> pour que les fichiers de cette version composée
/// (jar, libs, assets vanilla) soient réellement téléchargés.
/// </summary>
public sealed class ForgeManager : IForgeManager
{
    public async Task<string> EnsureForgeInstalledAsync(
        MinecraftLauncher launcher,
        string minecraftVersion,
        string forgeVersion,
        IProgress<string>? progress = null,
        IProgress<double>? downloadProgress = null,
        CancellationToken cancellationToken = default)
    {
        var forgeInstaller = new ForgeInstaller(launcher);

        var fileProgress = new Progress<InstallerProgressChangedEventArgs>(e =>
            progress?.Report($"[{e.ProgressedTasks}/{e.TotalTasks}] {e.Name}"));
        var byteProgress = new Progress<ByteProgress>(e =>
        {
            progress?.Report($"Téléchargement... {e.ToRatio():P0}");
            downloadProgress?.Report(e.ToRatio());
        });

        progress?.Report($"Installation de Forge {minecraftVersion}-{forgeVersion}...");

        var versionId = await forgeInstaller.Install(minecraftVersion, forgeVersion, new ForgeInstallOptions
        {
            FileProgress = fileProgress,
            ByteProgress = byteProgress,
        });

        progress?.Report("Installation des fichiers de la version (vanilla + Forge)...");
        await launcher.InstallAsync(versionId, fileProgress, byteProgress);

        return versionId;
    }
}
