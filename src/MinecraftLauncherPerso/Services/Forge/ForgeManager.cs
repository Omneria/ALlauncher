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

        var fileProgress = new Relay<InstallerProgressChangedEventArgs>(e =>
            progress?.Report($"[{e.ProgressedTasks}/{e.TotalTasks}] {e.Name}"));
        var byteProgress = new Relay<ByteProgress>(e =>
        {
            // ToRatio() vaut 0/0 = NaN tant que la taille totale est inconnue (TotalBytes = 0) :
            // ni "NaN %" dans le journal, ni NaN envoyé à la barre de progression (qui lève sur
            // NaN, voir LaunchProgress). Rien n'est rapporté tant que la taille n'est pas connue.
            if (e.TotalBytes <= 0)
            {
                return;
            }

            var ratio = e.ToRatio();
            if (!double.IsFinite(ratio))
            {
                return;
            }

            progress?.Report($"Téléchargement... {ratio:P0}");
            downloadProgress?.Report(ratio);
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

    /// <summary>Relaie chaque rapport de CmlLib immédiatement, sans Progress&lt;T&gt; (qui poste chaque
    /// rapport au contexte de synchronisation : des milliers de messages par seconde pendant un
    /// téléchargement). L'IProgress de l'appelant doit être thread-safe (voir CoalescingProgress).</summary>
    private sealed class Relay<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
