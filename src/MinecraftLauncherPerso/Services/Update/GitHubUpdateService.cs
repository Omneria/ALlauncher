using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
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

    // Requête conditionnelle (If-None-Match / 304) : l'API GitHub non authentifiée est limitée à
    // 60 requêtes par heure et par IP, or ce service est interrogé toutes les minutes (voir
    // "Cadence de rafraîchissement" dans le README) — sans ça, la moitié du quota partait ici,
    // l'autre moitié dans ReleaseChangelogService, et tout tombait en 403 au bout de 30 minutes
    // (mise à jour indétectable pour le reste de l'heure). Une réponse 304 ne compte pas dans le
    // quota : on renvoie alors le dernier résultat calculé, sans re-parser quoi que ce soit.
    private string? _cachedETag;
    private UpdateInfo? _cachedResult;

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
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApiUrl);
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            if (_cachedETag is not null)
            {
                request.Headers.IfNoneMatch.ParseAdd(_cachedETag);
            }

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                return _cachedResult;
            }

            if (!response.IsSuccessStatusCode)
            {
                Logger.Warn("GitHubUpdateService", $"Vérification de mise à jour refusée : HTTP {(int)response.StatusCode} {response.StatusCode}.");
                return null;
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
            var result = ParseLatestRelease(doc.RootElement);

            _cachedETag = response.Headers.ETag?.ToString();
            _cachedResult = result;
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
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

    private static UpdateInfo? ParseLatestRelease(JsonElement release)
    {
        var tagName = release.GetProperty("tag_name").GetString();
        if (tagName is null || !TryParseVersion(tagName, out var remoteVersion))
        {
            return null;
        }

        if (remoteVersion <= GetCurrentVersion())
        {
            return null;
        }

        var assets = release.GetProperty("assets").EnumerateArray().ToList();
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

    public async Task ApplyUpdateAndRestartAsync(UpdateInfo update, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var currentExePath = Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new InvalidOperationException("Impossible de déterminer le chemin de l'exécutable courant.");

        progress?.Report("Téléchargement de la mise à jour...");
        var newExePath = Path.Combine(Path.GetTempPath(), $"AL Launcher.new.{Guid.NewGuid():N}.exe");

        using (var response = await _httpClient.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            response.EnsureSuccessStatusCode();
            var totalBytes = response.Content.Headers.ContentLength ?? -1L;

            // Même filet que pour le modpack et Java (DiskSpaceChecker) : l'exe temporaire et
            // l'exe final coexistent le temps du remplacement, d'où le facteur x2.
            if (totalBytes > 0 && !DiskSpaceChecker.HasEnoughFreeSpace(Path.GetTempPath(), totalBytes * 2))
            {
                throw new InvalidOperationException(
                    $"Espace disque insuffisant pour télécharger la mise à jour (environ {DiskSpaceChecker.FormatBytes(totalBytes * 2)} nécessaires).");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var file = new FileStream(newExePath, FileMode.Create, FileAccess.Write, FileShare.None);

            var buffer = new byte[81920];
            long totalRead = 0;
            int bytesRead;
            var lastReportedPercent = -1;

            while ((bytesRead = await stream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                totalRead += bytesRead;

                if (totalBytes > 0)
                {
                    var percent = (int)(totalRead * 100 / totalBytes);
                    if (percent != lastReportedPercent)
                    {
                        lastReportedPercent = percent;
                        progress?.Report($"Téléchargement de la mise à jour... {percent}%");
                    }
                }
            }
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
        // qu'il tourne. Un script détaché attend sa fermeture (boucle sur tasklist/PID — le filtre
        // "PID eq" ne laisse passer que notre propre ligne, donc "find" ne peut pas matcher un
        // autre process par coïncidence), remplace le fichier, puis relance le launcher.
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
