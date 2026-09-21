using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using MinecraftLauncherPerso.Services.Diagnostics;

namespace MinecraftLauncherPerso.Services.Update;

/// <summary>
/// Vérifie et applique les mises à jour du launcher via les GitHub Releases du dépôt (publiées
/// par le workflow CI quand un tag v*.*.* est poussé, voir .github/workflows/build-windows.yml) :
/// pas de serveur de mise à jour dédié à héberger, GitHub sert directement les .exe en public.
/// </summary>
public sealed class GitHubUpdateService : IUpdateService
{
    private const string LatestReleaseApiUrl = "https://api.github.com/repos/Omneria/ALlauncher/releases/latest";

    private readonly HttpClient _httpClient;

    public GitHubUpdateService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();

        // L'API GitHub rejette (403) toute requête sans User-Agent.
        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("ALLauncher-UpdateChecker");
        }
    }

    public async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync(LatestReleaseApiUrl, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
            var tagName = doc.RootElement.GetProperty("tag_name").GetString();
            if (tagName is null || !TryParseVersion(tagName, out var remoteVersion))
            {
                return null;
            }

            if (remoteVersion <= GetCurrentVersion())
            {
                return null;
            }

            var assets = doc.RootElement.GetProperty("assets").EnumerateArray().ToList();
            var asset = assets.FirstOrDefault(
                a => (a.GetProperty("name").GetString() ?? "").EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

            if (asset.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var downloadUrl = asset.GetProperty("browser_download_url").GetString();
            if (string.IsNullOrEmpty(downloadUrl))
            {
                return null;
            }

            // Empreinte SHA-256 de l'exe, publiée par le workflow de release comme un fichier
            // séparé "<nom de l'exe>.sha256" à côté de l'exe lui-même. Absente sur d'anciennes
            // releases publiées avant l'ajout de cette vérification : ChecksumUrl reste alors null,
            // et ApplyUpdateAndRestartAsync applique la mise à jour sans contrôle plutôt que de
            // casser l'auto-update pour tout le monde rétroactivement.
            var exeAssetName = asset.GetProperty("name").GetString() ?? "";
            var checksumAsset = assets.FirstOrDefault(
                a => (a.GetProperty("name").GetString() ?? "") == $"{exeAssetName}.sha256");
            var checksumUrl = checksumAsset.ValueKind == JsonValueKind.Object
                ? checksumAsset.GetProperty("browser_download_url").GetString()
                : null;

            return new UpdateInfo(remoteVersion, downloadUrl, checksumUrl);
        }
        catch (Exception ex)
        {
            // Vérification best-effort : ne doit jamais empêcher de lancer le jeu. Reste tracée
            // dans le journal (auparavant invisible) pour distinguer "pas de mise à jour" d'un
            // échec répété (ex. rate-limit GitHub non authentifié) qui masquerait une vraie mise à
            // jour disponible sans que personne ne le sache.
            Logger.Warn("GitHubUpdateService", $"Vérification de mise à jour échouée : {ex.Message}");
            return null;
        }
    }

    public async Task ApplyUpdateAndRestartAsync(UpdateInfo update, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var currentExePath = Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new InvalidOperationException("Impossible de déterminer le chemin de l'exécutable courant.");

        progress?.Report("Téléchargement de la mise à jour...");
        var newExePath = Path.Combine(Path.GetTempPath(), $"AL Launcher.new.{Guid.NewGuid():N}.exe");

        await using (var stream = await _httpClient.GetStreamAsync(update.DownloadUrl, cancellationToken))
        await using (var file = new FileStream(newExePath, FileMode.Create, FileAccess.Write))
        {
            await stream.CopyToAsync(file, cancellationToken);
        }

        // Vérifie l'empreinte SHA-256 avant de remplacer/exécuter quoi que ce soit : sans ça, un
        // contenu altéré en transit (MITM, CDN GitHub compromis) aurait été exécuté avec les mêmes
        // droits que le launcher, sans aucun contrôle d'intégrité.
        if (!string.IsNullOrEmpty(update.ChecksumUrl))
        {
            progress?.Report("Vérification de l'intégrité...");
            var expectedSha256 = (await _httpClient.GetStringAsync(update.ChecksumUrl, cancellationToken)).Trim();

            string actualSha256;
            await using (var verifyStream = File.OpenRead(newExePath))
            {
                actualSha256 = Convert.ToHexString(await SHA256.HashDataAsync(verifyStream, cancellationToken)).ToLowerInvariant();
            }

            if (!string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(newExePath);
                throw new InvalidOperationException(
                    $"Empreinte SHA-256 invalide pour la mise à jour téléchargée : attendu {expectedSha256}, obtenu {actualSha256}. " +
                    "Fichier supprimé, le téléchargement a peut-être été altéré en transit.");
            }
        }

        // Le process courant verrouille son propre .exe (Windows) : impossible de l'écraser tant
        // qu'il tourne. Un script détaché attend sa fermeture (boucle sur tasklist/PID), remplace
        // le fichier, puis relance le launcher.
        var scriptPath = Path.Combine(Path.GetTempPath(), $"al-launcher-update-{Guid.NewGuid():N}.cmd");
        var script = $"""
            @echo off
            :wait
            tasklist /fi "PID eq {Environment.ProcessId}" | find "{Environment.ProcessId}" >nul
            if not errorlevel 1 (
                timeout /t 1 /nobreak >nul
                goto wait
            )
            move /y "{newExePath}" "{currentExePath}" >nul
            start "" "{currentExePath}"
            del "%~f0"
            """;
        await File.WriteAllTextAsync(scriptPath, script, cancellationToken);

        progress?.Report("Redémarrage du launcher...");
        Process.Start(new ProcessStartInfo(scriptPath)
        {
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            CreateNoWindow = true,
        });

        Environment.Exit(0);
    }

    private static Version GetCurrentVersion() =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    // internal (au lieu de private) : testé directement par MinecraftLauncherPerso.Tests (voir
    // InternalsVisibleTo dans le csproj) sans avoir besoin de passer par toute la vérification
    // réseau pour valider le parsing des différents formats de tag.
    internal static bool TryParseVersion(string tagName, out Version version)
    {
        var trimmed = tagName.TrimStart('v', 'V');
        if (Version.TryParse(trimmed, out var parsed))
        {
            version = parsed;
            return true;
        }

        version = new Version(0, 0, 0);
        return false;
    }
}
